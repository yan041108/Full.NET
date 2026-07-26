import { spawnSync } from 'node:child_process';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';

const stableOperationFields = [
  'permission',
  'successStatus',
  'requestSchema',
  'responseSchema'
];

function formatValue(value) {
  return value === undefined ? '<missing>' : JSON.stringify(value);
}

function indexPaths(contract) {
  return new Map(
    (Array.isArray(contract.paths) ? contract.paths : []).map((entry) => [
      entry.path,
      entry
    ])
  );
}

function indexOperations(pathEntry) {
  return new Map(
    (
      Array.isArray(pathEntry.operations) ? pathEntry.operations : []
    ).map((operation) => [String(operation.method).toUpperCase(), operation])
  );
}

function validateContractIdentities(contracts, changes) {
  const identities = new Map();

  for (const [fileName, contract] of contracts) {
    const hasId = Object.hasOwn(contract, 'id');
    const hasVersion = Object.hasOwn(contract, 'version');
    if (!hasId && !hasVersion) {
      continue;
    }

    if (
      typeof contract.id !== 'string' ||
      contract.id.trim().length === 0 ||
      !Number.isInteger(contract.version) ||
      contract.version < 1
    ) {
      changes.push(
        `invalid contract identity: ${fileName} requires a non-empty ` +
          'string id and positive integer version'
      );
      continue;
    }

    const identityKey = JSON.stringify([contract.id, contract.version]);
    const existingFileName = identities.get(identityKey);
    if (existingFileName) {
      changes.push(
        `duplicate contract identity: ${existingFileName} and ${fileName} ` +
          `use id=${String(contract.id)}, version=${String(contract.version)}`
      );
      continue;
    }

    identities.set(identityKey, fileName);
  }
}

function compareStructuredContract(fileName, baseline, current, changes) {
  for (const fieldName of Object.keys(baseline)) {
    if (
      fieldName === 'description' ||
      fieldName === 'paths' ||
      fieldName === 'schemas'
    ) {
      continue;
    }

    if (!isDeepStrictEqual(baseline[fieldName], current[fieldName])) {
      changes.push(
        `stable setting changed: ${fileName} ${fieldName} ` +
          `(baseline=${formatValue(baseline[fieldName])}, ` +
          `current=${formatValue(current[fieldName])})`
      );
    }
  }

  const currentPaths = indexPaths(current);
  for (const baselinePath of Array.isArray(baseline.paths)
    ? baseline.paths
    : []) {
    const currentPath = currentPaths.get(baselinePath.path);
    if (!currentPath) {
      changes.push(`path removed: ${fileName} ${baselinePath.path}`);
      continue;
    }

    const currentOperations = indexOperations(currentPath);
    for (const baselineOperation of Array.isArray(baselinePath.operations)
      ? baselinePath.operations
      : []) {
      const method = String(baselineOperation.method).toUpperCase();
      const currentOperation = currentOperations.get(method);
      if (!currentOperation) {
        changes.push(
          `operation removed: ${fileName} ${method} ${baselinePath.path}`
        );
        continue;
      }

      for (const fieldName of stableOperationFields) {
        if (
          !isDeepStrictEqual(
            baselineOperation[fieldName],
            currentOperation[fieldName]
          )
        ) {
          changes.push(
            `operation changed: ${fileName} ${method} ` +
              `${baselinePath.path} ${fieldName} ` +
              `(baseline=${formatValue(baselineOperation[fieldName])}, ` +
              `current=${formatValue(currentOperation[fieldName])})`
          );
        }
      }
    }
  }

  const baselineSchemas = baseline.schemas ?? {};
  const currentSchemas = current.schemas ?? {};
  for (const [schemaName, baselineSchema] of Object.entries(baselineSchemas)) {
    const currentSchema = currentSchemas[schemaName];
    if (!currentSchema) {
      changes.push(`schema removed: ${fileName} ${schemaName}`);
      continue;
    }

    const currentProperties = new Set(
      Array.isArray(currentSchema.properties) ? currentSchema.properties : []
    );
    for (const propertyName of Array.isArray(baselineSchema.properties)
      ? baselineSchema.properties
      : []) {
      if (!currentProperties.has(propertyName)) {
        changes.push(
          `schema property removed: ${fileName} ${schemaName}.${propertyName}`
        );
      }
    }

    if (!isDeepStrictEqual(baselineSchema.itemSchema, currentSchema.itemSchema)) {
      changes.push(
        `schema itemSchema changed: ${fileName} ${schemaName} ` +
          `(baseline=${formatValue(baselineSchema.itemSchema)}, ` +
          `current=${formatValue(currentSchema.itemSchema)})`
      );
    }
  }
}

function compareStableSettings(fileName, baseline, current, changes) {
  for (const [fieldName, baselineValue] of Object.entries(baseline)) {
    if (fieldName === 'description') {
      continue;
    }

    if (!isDeepStrictEqual(baselineValue, current[fieldName])) {
      changes.push(
        `stable setting changed: ${fileName} ${fieldName} ` +
          `(baseline=${formatValue(baselineValue)}, ` +
          `current=${formatValue(current[fieldName])})`
      );
    }
  }
}

export function compareContractSets(baselineContracts, currentContracts) {
  const changes = [];

  validateContractIdentities(currentContracts, changes);

  for (const [fileName, baseline] of baselineContracts) {
    const current = currentContracts.get(fileName);
    if (!current) {
      changes.push(`contract removed: ${fileName}`);
      continue;
    }

    if (Array.isArray(baseline.paths) && baseline.schemas) {
      compareStructuredContract(fileName, baseline, current, changes);
    } else {
      compareStableSettings(fileName, baseline, current, changes);
    }
  }

  return changes.sort((left, right) => left.localeCompare(right, 'en'));
}

async function parseContract(fileName, content) {
  try {
    return JSON.parse(content);
  } catch (error) {
    throw new Error(`Invalid OpenAPI contract JSON in ${fileName}: ${error.message}`);
  }
}

export async function loadContractsFromDirectory(directoryPath) {
  const entries = await readdir(directoryPath, { withFileTypes: true });
  const fileNames = entries
    .filter((entry) => entry.isFile() && entry.name.endsWith('.json'))
    .map((entry) => entry.name)
    .sort((left, right) => left.localeCompare(right, 'en'));
  const contracts = new Map();

  for (const fileName of fileNames) {
    const content = await readFile(path.join(directoryPath, fileName), 'utf8');
    contracts.set(fileName, await parseContract(fileName, content));
  }

  return contracts;
}

function runGit(repositoryRoot, argumentsList) {
  return spawnSync('git', ['-C', repositoryRoot, ...argumentsList], {
    encoding: 'utf8',
    maxBuffer: 16 * 1024 * 1024
  });
}

export async function loadContractsAtGitRef(repositoryRoot, baseRef) {
  const listResult = runGit(repositoryRoot, [
    'ls-tree',
    '-r',
    '--name-only',
    baseRef,
    '--',
    'contracts/openapi'
  ]);

  if (listResult.status !== 0) {
    const detail = listResult.stderr.trim() || `git exited ${listResult.status}`;
    throw new Error(
      `Unable to load OpenAPI baseline from Git ref "${baseRef}": ${detail}`
    );
  }

  const relativePaths = listResult.stdout
    .split(/\r?\n/u)
    .filter((relativePath) => relativePath.endsWith('.json'))
    .sort((left, right) => left.localeCompare(right, 'en'));
  const contracts = new Map();

  for (const relativePath of relativePaths) {
    const showResult = runGit(repositoryRoot, [
      'show',
      `${baseRef}:${relativePath}`
    ]);
    if (showResult.status !== 0) {
      const detail = showResult.stderr.trim() || `git exited ${showResult.status}`;
      throw new Error(
        `Unable to load OpenAPI baseline from Git ref "${baseRef}": ${detail}`
      );
    }

    const fileName = path.basename(relativePath);
    contracts.set(fileName, await parseContract(relativePath, showResult.stdout));
  }

  return contracts;
}
