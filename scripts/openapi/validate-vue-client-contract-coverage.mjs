import { readFile, readdir, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const defaultRepositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

const manifestRelativePath = 'contracts/openapi/vue-client-coverage-v1.json';
const clientGenerationManifestRelativePath = 'contracts/openapi/client-generation-manifest-v1.json';
const clientOpenApiSnapshotRelativePath = 'contracts/openapi/fullnet-client-v1.openapi.json';
const apiDirectoryRelativePath = 'ui/admin/src/api';
const clientContractsIndexRelativePath = 'packages/client-contracts/src/index.ts';
const allowedLocalInterfaceSuffix = /(Params|Options|Filters?|Query)$/u;
const forbiddenExemptionPattern = /\b(manual|legacy|TODO)\b/u;

export async function collectProductionApiModules(repositoryRoot = defaultRepositoryRoot) {
  const apiDirectory = path.join(repositoryRoot, apiDirectoryRelativePath);
  const entries = await readdir(apiDirectory);
  const modules = [];

  for (const entry of entries) {
    if (!entry.endsWith('.ts') || entry.endsWith('.test.ts') || entry === 'http.ts') {
      continue;
    }
    modules.push(path.posix.join(apiDirectoryRelativePath, entry));
  }

  return modules.sort((left, right) => left.localeCompare(right));
}

function normalizePosix(relativePath) {
  return relativePath.split(path.sep).join('/');
}

function collectFixturePaths(fixture) {
  if (Array.isArray(fixture.paths)) {
    return fixture.paths.map((entry) => entry.path);
  }
  if (fixture.paths && typeof fixture.paths === 'object') {
    return Object.keys(fixture.paths);
  }
  if (typeof fixture.path === 'string') {
    return [fixture.path];
  }
  return [];
}

function fixtureHasRoutePrefix(fixture, routePrefix) {
  return collectFixturePaths(fixture).some((entryPath) =>
    entryPath === routePrefix || entryPath.startsWith(`${routePrefix}/`));
}

function apiSourceHasRoutePrefix(source, routePrefix) {
  return source.includes(routePrefix);
}

function collectOperationPaths(document) {
  const operationPaths = new Map();
  for (const [operationPath, pathItem] of Object.entries(document.paths ?? {})) {
    for (const operation of Object.values(pathItem ?? {})) {
      if (typeof operation?.operationId === 'string') {
        operationPaths.set(operation.operationId, operationPath);
      }
    }
  }
  return operationPaths;
}

function generatedSourceHasRoutePrefix(
  apiModule,
  apiSource,
  routePrefix,
  generatedEntriesByModule,
  generatedOperationPaths
) {
  const entries = generatedEntriesByModule.get(apiModule) ?? [];
  return entries.some((entry) => {
    const operationPath = generatedOperationPaths.get(entry.operationId);
    return apiSource.includes(entry.operationId)
      && (operationPath === routePrefix || operationPath?.startsWith(`${routePrefix}/`));
  });
}

function collectForbiddenLocalInterfaces(source) {
  const violations = [];
  const pattern = /export interface (\w+)([^{]*)\{/gu;
  for (const match of source.matchAll(pattern)) {
    const name = match[1];
    const header = match[2] ?? '';
    if (allowedLocalInterfaceSuffix.test(name)) {
      continue;
    }
    if (/\bextends\b/u.test(header)) {
      continue;
    }
    violations.push(name);
  }
  return violations;
}

function collectClientContractExportViolations(indexSource, clientContractModules) {
  const violations = [];
  for (const modulePath of clientContractModules) {
    const posixPath = normalizePosix(modulePath);
    const fileName = path.posix.basename(posixPath, '.ts');
  const exportMarkers = [
      `from './${fileName}.js'`,
      `from "./${fileName}.js"`
    ];
    if (!exportMarkers.some((marker) => indexSource.includes(marker))) {
      violations.push(`${modulePath} is not exported from ${clientContractsIndexRelativePath}`);
    }
  }
  return violations;
}

export async function validateVueClientContractCoverage(repositoryRoot = defaultRepositoryRoot) {
  const root = path.resolve(repositoryRoot);
  const violations = [];

  const manifestPath = path.join(root, manifestRelativePath);
  let manifest;
  try {
    manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
  } catch (error) {
    violations.push(`Unable to read manifest ${manifestRelativePath}: ${error.message}`);
    return violations;
  }

  if (manifest.schemaVersion !== 1 || !Array.isArray(manifest.entries)) {
    violations.push('Manifest must define schemaVersion: 1 and entries: []');
    return violations;
  }

  if (forbiddenExemptionPattern.test(JSON.stringify(manifest))) {
    violations.push('Manifest must not contain manual, legacy, or TODO exemptions');
  }

  let clientGenerationManifest;
  let clientOpenApiSnapshot;
  try {
    clientGenerationManifest = JSON.parse(await readFile(
      path.join(root, clientGenerationManifestRelativePath),
      'utf8'
    ));
    clientOpenApiSnapshot = JSON.parse(await readFile(
      path.join(root, clientOpenApiSnapshotRelativePath),
      'utf8'
    ));
  } catch (error) {
    violations.push(`Unable to read generated client coverage inputs: ${error.message}`);
    return violations;
  }

  const generatedEntriesByModule = new Map();
  for (const generatedEntry of clientGenerationManifest.entries ?? []) {
    const entries = generatedEntriesByModule.get(generatedEntry.apiModule) ?? [];
    entries.push(generatedEntry);
    generatedEntriesByModule.set(generatedEntry.apiModule, entries);
  }
  const generatedOperationPaths = collectOperationPaths(clientOpenApiSnapshot);

  const productionModules = await collectProductionApiModules(root);
  const manifestByModule = new Map(manifest.entries.map((entry) => [entry.apiModule, entry]));

  for (const apiModule of productionModules) {
    if (!manifestByModule.has(apiModule)) {
      violations.push(`Missing manifest entry for ${apiModule}`);
    }
  }

  for (const entry of manifest.entries) {
    if (!productionModules.includes(entry.apiModule)) {
      violations.push(`Manifest entry references non-production API module: ${entry.apiModule}`);
      continue;
    }

    for (const key of ['apiModule', 'openApiFixture']) {
      if (typeof entry[key] !== 'string' || entry[key].includes('*')) {
        violations.push(`${entry.apiModule}: invalid ${key}`);
      }
    }

    if (!Array.isArray(entry.routePrefixes) || entry.routePrefixes.length === 0) {
      violations.push(`${entry.apiModule}: routePrefixes must be a non-empty array`);
      continue;
    }

    if (!Array.isArray(entry.clientContractModules) || entry.clientContractModules.length === 0) {
      violations.push(`${entry.apiModule}: clientContractModules must be a non-empty array`);
      continue;
    }

    const apiSourcePath = path.join(root, entry.apiModule);
    const fixturePath = path.join(root, entry.openApiFixture);
    let apiSource;
    let fixtureSource;
    try {
      apiSource = await readFile(apiSourcePath, 'utf8');
    } catch {
      violations.push(`${entry.apiModule}: API module file is missing`);
      continue;
    }

    try {
      await stat(fixturePath);
      fixtureSource = await readFile(fixturePath, 'utf8');
    } catch {
      violations.push(`${entry.apiModule}: OpenAPI fixture is missing at ${entry.openApiFixture}`);
      continue;
    }

    if (!apiSource.includes('@fullnet/client-contracts')) {
      violations.push(`${entry.apiModule}: must import DTO/guards from @fullnet/client-contracts`);
    }

    if (/request<(?!\s*unknown\s*>)/u.test(apiSource)) {
      violations.push(`${entry.apiModule}: request<T> must only use unknown with runtime guards`);
    }

    const localInterfaces = collectForbiddenLocalInterfaces(apiSource);
    for (const localInterface of localInterfaces) {
      violations.push(`${entry.apiModule}: local backend-shaped interface ${localInterface} is not allowed`);
    }

    let fixture;
    try {
      fixture = JSON.parse(fixtureSource);
    } catch (error) {
      violations.push(`${entry.openApiFixture}: invalid JSON (${error.message})`);
      continue;
    }

    for (const routePrefix of entry.routePrefixes) {
      if (!apiSourceHasRoutePrefix(apiSource, routePrefix)
        && !generatedSourceHasRoutePrefix(
          entry.apiModule,
          apiSource,
          routePrefix,
          generatedEntriesByModule,
          generatedOperationPaths
        )) {
        violations.push(`${entry.apiModule}: route prefix ${routePrefix} is not covered by API source or generated operation binding`);
      }
      if (!fixtureHasRoutePrefix(fixture, routePrefix)) {
        violations.push(`${entry.apiModule}: route prefix ${routePrefix} is not present in ${entry.openApiFixture}`);
      }
    }

    for (const modulePath of entry.clientContractModules) {
      const absoluteModulePath = path.join(root, modulePath);
      try {
        await stat(absoluteModulePath);
      } catch {
        violations.push(`${entry.apiModule}: client contract module is missing at ${modulePath}`);
      }
    }

    const indexSource = await readFile(path.join(root, clientContractsIndexRelativePath), 'utf8');
    violations.push(...collectClientContractExportViolations(indexSource, entry.clientContractModules));
  }

  const duplicateModules = manifest.entries
    .map((entry) => entry.apiModule)
    .filter((apiModule, index, all) => all.indexOf(apiModule) !== index);
  for (const apiModule of new Set(duplicateModules)) {
    violations.push(`Duplicate manifest entry for ${apiModule}`);
  }

  return violations;
}

async function main() {
  const violations = await validateVueClientContractCoverage();
  if (violations.length > 0) {
    console.error('Vue client contract coverage validation failed:');
    for (const violation of violations) {
      console.error(`- ${violation}`);
    }
    process.exitCode = 1;
    return;
  }

  console.log('Vue client contract coverage validation passed.');
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  main().catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
}
