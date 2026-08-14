#!/usr/bin/env node

/**
 * Make one authenticated REST API call to the BDP endpoint.
 *
 * This script is a minimal transport check and first data read for consumers.
 * It supports three resources:
 *
 * - organizations
 * - sites
 * - buildings (requires --site-id)
 *
 * Usage:
 *     node call_rest_api.js --resource sites --take 5
 *     node call_rest_api.js --resource organizations
 *     node call_rest_api.js --resource buildings --site-id YOUR_SITE_GUID
 */

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import { parseArgs } from 'util';
import https from 'https';
import { URL } from 'url';
import dotenv from 'dotenv';

const TOKEN_ENV = 'BDP_API_TOKEN';
const UAT_BASE_URL = 'https://ecostruxure-building-platform-api-uat.se.app';
const RESOURCE_TO_PATH = {
  organizations: '/api/Organizations',
  sites: '/api/Sites',
  buildings: '/api/Buildings',
};

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

/**
 * Read token from a dotenv file, or null when absent/unreadable.
 */
function tokenFromDotenv(dotenvPath) {
  try {
    const content = fs.readFileSync(dotenvPath, 'utf-8');
    for (const rawLine of content.split('\n')) {
      const line = rawLine.trim();
      if (!line || line.startsWith('#') || !line.includes('=')) {
        continue;
      }
      const [name, ...valueParts] = line.split('=');
      if (name.trim() !== TOKEN_ENV) {
        continue;
      }
      let token = valueParts.join('=').trim();
      if (
        (token.startsWith('"') && token.endsWith('"')) ||
        (token.startsWith("'") && token.endsWith("'"))
      ) {
        token = token.slice(1, -1);
      }
      return token || null;
    }
  } catch (err) {
    // File not readable or doesn't exist
    return null;
  }
  return null;
}

/**
 * Read token from env first, then fallback to .env in local/script directory.
 */
function resolveToken() {
  const token = process.env[TOKEN_ENV];
  if (token) {
    return token;
  }

  const scriptDirEnv = path.join(__dirname, '.env');
  const cwdEnv = path.join(process.cwd(), '.env');

  let resolvedToken = tokenFromDotenv(scriptDirEnv);
  if (resolvedToken) {
    return resolvedToken;
  }

  // Support running from a directory different from this script's folder.
  if (path.resolve(scriptDirEnv) !== path.resolve(cwdEnv)) {
    return tokenFromDotenv(cwdEnv);
  }
  return null;
}

/**
 * Build the full URL with query parameters.
 */
function buildUrl(baseUrl, resource, siteId, take, skip) {
  const resourcePath = RESOURCE_TO_PATH[resource];
  if (!resourcePath) {
    throw new Error(`Unknown resource: ${resource}`);
  }

  if (resource === 'buildings' && !siteId) {
    throw new Error('--site-id is required when --resource buildings is used');
  }

  const query = new URLSearchParams();
  query.append('take', String(take));
  query.append('skip', String(skip));

  if (resource === 'buildings') {
    query.append('siteId', siteId);
  }

  const url = new URL(baseUrl);
  url.pathname = resourcePath;
  url.search = query.toString();
  return url.toString();
}

/**
 * Print a compact, human-first summary of known response shapes.
 */
function summarizeJson(payload) {
  if (Array.isArray(payload)) {
    console.log(`items: ${payload.length}`);
    for (const item of payload.slice(0, 10)) {
      if (typeof item === 'object' && item !== null) {
        const itemId = item.id ?? '-';
        const itemName = item.name ?? '-';
        console.log(`  - ${itemId} | ${itemName}`);
      } else {
        console.log(`  - ${item}`);
      }
    }
    return;
  }

  if (typeof payload === 'object' && payload !== null) {
    if (Array.isArray(payload.items)) {
      console.log(`items: ${payload.items.length}`);
      for (const item of payload.items.slice(0, 10)) {
        if (typeof item === 'object' && item !== null) {
          const itemId = item.id ?? '-';
          const itemName = item.name ?? '-';
          console.log(`  - ${itemId} | ${itemName}`);
        } else {
          console.log(`  - ${item}`);
        }
      }
      return;
    }

    const fields = Object.keys(payload).sort().join(', ');
    console.log(`top-level fields: ${fields}`);
    return;
  }

  console.log(typeof payload);
}

/**
 * Make an HTTPS request and return the response status and parsed JSON body.
 */
async function requestJson(url, token, apiVersion, timeout) {
  return new Promise((resolve, reject) => {
    const urlObj = new URL(url);

    const options = {
      hostname: urlObj.hostname,
      port: urlObj.port || 443,
      path: urlObj.pathname + urlObj.search,
      method: 'GET',
      headers: {
        Authorization: `Bearer ${token}`,
        'X-Api-Version': apiVersion,
        Accept: 'application/json',
      },
      timeout,
    };

    const req = https.request(options, (res) => {
      let data = '';

      res.on('data', (chunk) => {
        data += chunk;
      });

      res.on('end', () => {
        let payload;
        try {
          payload = data ? JSON.parse(data) : {};
        } catch (err) {
          payload = { raw: data };
        }
        resolve({ status: res.statusCode, payload });
      });
    });

    req.on('error', (err) => {
      reject(err);
    });

    req.on('timeout', () => {
      req.destroy();
      reject(new Error('Request timeout'));
    });

    req.end();
  });
}

/**
 * Print error message based on HTTP status code and body.
 */
function printError(code, body) {
  const snippet = body.substring(0, 400).trim().replace(/\n/g, ' ');

  if (code === 401) {
    console.error(
      'HTTP 401: token expired or malformed. Copy a fresh token from the portal.'
    );
    return;
  }
  if (code === 403 && body.includes('Application-Gateway')) {
    console.error(
      'HTTP 403: refused before the API. The request did not reach the API.'
    );
    return;
  }
  if (code === 400 && body.includes('Unsupported API Version')) {
    console.error('HTTP 400: unsupported API version. Use 2.0 or 3.0.');
    return;
  }

  console.error(`HTTP ${code}: ${snippet}`);
}

/**
 * Parse command-line arguments.
 */
async function main(argv) {
  const options = {
    resource: {
      type: 'string',
      default: 'sites',
      short: 'r',
    },
    'site-id': {
      type: 'string',
      short: 's',
    },
    take: {
      type: 'string',
      default: '5',
    },
    skip: {
      type: 'string',
      default: '0',
    },
    'base-url': {
      type: 'string',
      default: UAT_BASE_URL,
    },
    'api-version': {
      type: 'string',
      default: '3.0',
    },
    timeout: {
      type: 'string',
      default: '30',
    },
    raw: {
      type: 'boolean',
      default: false,
    },
    help: {
      type: 'boolean',
      short: 'h',
    },
  };

  let parsed;
  try {
    parsed = parseArgs({
      args: argv,
      options,
      allowPositionals: false,
      strict: true,
    });
  } catch (err) {
    console.error(err.message);
    console.error(
      '\nUsage: node call_rest_api.js [--resource sites|organizations|buildings] [--take 5] [--skip 0] [--api-version 3.0] [--timeout 30] [--raw]'
    );
    process.exit(2);
  }

  if (parsed.values.help) {
    console.log('Usage: node call_rest_api.js [OPTIONS]');
    console.log('\nOptions:');
    console.log('  --resource organizations|sites|buildings   (default: sites)');
    console.log('  --site-id <GUID>                           required for buildings');
    console.log('  --take <int>                               (default: 5)');
    console.log('  --skip <int>                               (default: 0)');
    console.log('  --base-url <url>                           (default: UAT host)');
    console.log('  --api-version 2.0|3.0                      (default: 3.0)');
    console.log('  --timeout <seconds>                        (default: 30)');
    console.log('  --raw                                      print full JSON response');
    console.log('  --help                                     show this help message');
    process.exit(0);
  }

  const resource = parsed.values.resource.toLowerCase();
  const siteId = parsed.values['site-id'];
  const take = parseInt(parsed.values.take, 10);
  const skip = parseInt(parsed.values.skip, 10);
  const baseUrl = parsed.values['base-url'];
  const apiVersion = parsed.values['api-version'];
  const timeoutSeconds = parseInt(parsed.values.timeout, 10);
  const raw = parsed.values.raw;

  // Validate resource choice
  if (!RESOURCE_TO_PATH[resource]) {
    console.error(
      `--resource must be one of: ${Object.keys(RESOURCE_TO_PATH).join(', ')}`
    );
    process.exit(2);
  }

  // Validate numeric arguments
  if (isNaN(take) || take < 0 || isNaN(skip) || skip < 0) {
    console.error('--take and --skip must be non-negative integers');
    process.exit(2);
  }

  if (isNaN(timeoutSeconds) || timeoutSeconds <= 0) {
    console.error('--timeout must be a positive integer');
    process.exit(2);
  }

  // Validate API version
  if (apiVersion !== '2.0' && apiVersion !== '3.0') {
    console.error('--api-version must be 2.0 or 3.0');
    process.exit(2);
  }

  // Load .env file if it exists
  dotenv.config({ path: path.join(__dirname, '.env') });
  dotenv.config({ path: path.join(process.cwd(), '.env') });

  // Resolve token
  const token = resolveToken();
  if (!token) {
    console.error(
      `set ${TOKEN_ENV} in your environment or add it to a .env file in this folder`
    );
    process.exit(2);
  }

  // Build URL
  let url;
  try {
    url = buildUrl(baseUrl, resource, siteId, take, skip);
  } catch (err) {
    console.error(err.message);
    process.exit(2);
  }

  console.log(`GET ${url}`);

  // Make the request
  try {
    const { status, payload } = await requestJson(
      url,
      token,
      apiVersion,
      timeoutSeconds * 1000
    );

    if (status < 200 || status >= 300) {
      const body = typeof payload === 'string' ? payload : JSON.stringify(payload);
      printError(status, body);
      process.exit(1);
    }

    console.log(`status: ${status}`);
    if (raw) {
      console.log(JSON.stringify(payload, null, 2));
    } else {
      summarizeJson(payload);
    }

    process.exit(0);
  } catch (err) {
    if (err.message === 'Request timeout') {
      console.error('network error: request timed out');
    } else if (err.code === 'ENOTFOUND') {
      console.error(`network error: ${err.message}`);
    } else {
      console.error(`network error: ${err.message}`);
    }
    process.exit(1);
  }
}

// Run the script
main(process.argv.slice(2));
