import { readFile } from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const operationIdPattern = /^[a-z][A-Za-z0-9]*$/u;
const primaryTagPattern = /^[A-Z][A-Za-z0-9]*$/u;
const successStatusPattern = /^2(?:\d{2}|XX)$/iu;
const methods = ['get', 'post', 'put', 'patch', 'delete'];

export function validateClientGenerationReadiness(document, options = {}) {
  const violations = [];
  const operationIds = new Map();
  const publicOperationIds = new Set(options.publicOperationIds ?? []);
  const paths = isObject(document?.paths) ? document.paths : {};
  const securitySchemes = isObject(document?.components?.securitySchemes)
    ? document.components.securitySchemes
    : {};

  for (const pathName of Object.keys(paths).sort(compareText)) {
    const pathItem = paths[pathName];
    if (!isObject(pathItem)) {
      continue;
    }

    for (const method of methods) {
      const operation = pathItem[method];
      if (!isObject(operation)) {
        continue;
      }

      const location = `${method.toUpperCase()} ${pathName}`;
      validateOperationIdentity(operation, location, operationIds, violations);
      validatePrimaryTag(operation, location, violations);
      validateSecurity(
        operation,
        document?.security,
        securitySchemes,
        publicOperationIds,
        location,
        violations
      );
      validateResponses(operation, location, violations);
    }
  }

  return violations.sort(compareText);
}

function validateOperationIdentity(operation, location, operationIds, violations) {
  const operationId = operation.operationId;
  if (typeof operationId !== 'string' || operationId.length === 0) {
    violations.push(`${location}: missing operationId`);
    return;
  }

  if (!operationIdPattern.test(operationId)) {
    violations.push(`${location}: operationId must be lowerCamelCase`);
  }

  if (operationIds.has(operationId)) {
    violations.push(`${location}: duplicate operationId ${operationId}`);
    return;
  }

  operationIds.set(operationId, location);
}

function validatePrimaryTag(operation, location, violations) {
  const primaryTags = Array.isArray(operation.tags)
    ? operation.tags.filter(tag => typeof tag === 'string' && primaryTagPattern.test(tag))
    : [];
  if (primaryTags.length !== 1) {
    violations.push(`${location}: expected exactly one primary tag`);
  }
}

function validateSecurity(
  operation,
  documentSecurity,
  securitySchemes,
  publicOperationIds,
  location,
  violations
) {
  const security = Object.hasOwn(operation, 'security')
    ? operation.security
    : documentSecurity;
  const isExplicitPublicOperation = Array.isArray(security)
    && security.length === 0
    && publicOperationIds.has(operation.operationId);

  if (isExplicitPublicOperation) {
    return;
  }

  if (!Array.isArray(security) || security.length === 0
    || !security.some(requirement => isObject(requirement) && Object.keys(requirement).length > 0)) {
    violations.push(`${location}: protected API must declare security`);
    return;
  }

  const names = new Set();
  for (const requirement of security) {
    if (!isObject(requirement)) {
      continue;
    }
    for (const name of Object.keys(requirement)) {
      names.add(name);
    }
  }

  for (const name of [...names].sort(compareText)) {
    if (!Object.hasOwn(securitySchemes, name)) {
      violations.push(`${location}: security scheme ${name} is not defined`);
    }
  }
}

function validateResponses(operation, location, violations) {
  const responses = isObject(operation.responses) ? operation.responses : {};
  const successStatuses = Object.keys(responses)
    .filter(status => successStatusPattern.test(status))
    .sort(compareText);

  if (successStatuses.length === 0) {
    violations.push(`${location}: expected at least one 2xx response`);
    return;
  }

  for (const status of successStatuses) {
    const response = responses[status];
    if (!isObject(response) || !isObject(response.content)) {
      continue;
    }

    for (const mediaType of Object.keys(response.content).sort(compareText)) {
      if (!isJsonMediaType(mediaType)) {
        continue;
      }

      if (status === '204') {
        violations.push(`${location}: 204 response must not declare JSON content`);
        continue;
      }

      const media = response.content[mediaType];
      if (!isObject(media) || !Object.hasOwn(media, 'schema')) {
        violations.push(`${location}: ${status} ${mediaType} response must declare schema`);
        continue;
      }

      const schema = media.schema;
      if (!hasExplicitSchemaIdentity(schema)) {
        violations.push(`${location}: ${status} ${mediaType} schema must declare type or $ref`);
        continue;
      }

      if (isArraySchema(schema) && !Object.hasOwn(schema, 'items')) {
        violations.push(`${location}: ${status} ${mediaType} array schema must declare items`);
      }

      if (schema.type === 'string' && schema.format === 'binary') {
        violations.push(`${location}: ${status} binary response must not use JSON media type`);
      }
    }
  }
}

function hasExplicitSchemaIdentity(schema) {
  return isObject(schema)
    && (typeof schema.$ref === 'string'
      || typeof schema.type === 'string'
      || (Array.isArray(schema.type) && schema.type.length > 0));
}

function isArraySchema(schema) {
  return isObject(schema)
    && (schema.type === 'array'
      || (Array.isArray(schema.type) && schema.type.includes('array')));
}

function isJsonMediaType(mediaType) {
  const normalized = mediaType.split(';', 1)[0].trim().toLowerCase();
  return normalized === 'application/json' || normalized.endsWith('+json');
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function compareText(left, right) {
  return left.localeCompare(right, 'en');
}

async function runCli() {
  const documentPath = process.argv[2];
  if (!documentPath) {
    console.error('Usage: node scripts/openapi/validate-client-generation-readiness.mjs <openapi.json>');
    process.exitCode = 2;
    return;
  }

  try {
    const document = JSON.parse(await readFile(path.resolve(documentPath), 'utf8'));
    const violations = validateClientGenerationReadiness(document);
    for (const violation of violations) {
      console.error(violation);
    }
    process.exitCode = violations.length === 0 ? 0 : 1;
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 2;
  }
}

const isCli = process.argv[1]
  && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isCli) {
  await runCli();
}
