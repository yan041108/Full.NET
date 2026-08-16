import { readdir, readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const defaultRepositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

const scanRoots = [
  'ui/admin/src/views',
  'ui/admin/src/components',
  'ui/admin/src/preferences',
  'ui/admin/src/auth'
];

const allowlistPathPatterns = [
  /^ui\/admin\/src\/api\//u,
  /^ui\/admin\/src\/preferences\/grid-preferences\.ts$/u
];

const allowlistAuthInfrastructure = new Set([
  'ui/admin/src/auth/session.ts',
  'ui/admin/src/auth/session-refresh-coordinator.ts',
  'ui/admin/src/auth/session.test.ts'
]);

const forbiddenApiLiteral = /\/api\/v1\//u;
const forbiddenHttpImport = /from\s+['"][^'"]*\/api\/http['"]/u;

function normalizePosix(relativePath) {
  return relativePath.split(path.sep).join('/');
}

function isAllowlisted(relativePath) {
  const posixPath = normalizePosix(relativePath);
  if (allowlistAuthInfrastructure.has(posixPath)) {
    return true;
  }
  return allowlistPathPatterns.some(pattern => pattern.test(posixPath));
}

async function collectSourceFiles(directory, repositoryRoot, files = []) {
  const absoluteDirectory = path.join(repositoryRoot, directory);
  let entries;
  try {
    entries = await readdir(absoluteDirectory, { withFileTypes: true });
  } catch {
    return files;
  }

  for (const entry of entries) {
    const relativePath = path.posix.join(directory, entry.name);
    if (entry.isDirectory()) {
      await collectSourceFiles(relativePath, repositoryRoot, files);
      continue;
    }
    if (!/\.(vue|ts)$/u.test(entry.name) || entry.name.endsWith('.d.ts') || entry.name.endsWith('.test.ts')) {
      continue;
    }
    files.push(relativePath);
  }

  return files;
}

export async function validateVueApiCallSiteCoverage(repositoryRoot = defaultRepositoryRoot) {
  const root = path.resolve(repositoryRoot);
  const violations = [];
  const scannedFiles = [];

  for (const scanRoot of scanRoots) {
    scannedFiles.push(...await collectSourceFiles(scanRoot, root));
  }

  for (const relativePath of scannedFiles.sort()) {
    const posixPath = normalizePosix(relativePath);
    if (isAllowlisted(posixPath)) {
      continue;
    }

    const source = await readFile(path.join(root, relativePath), 'utf8');
    if (forbiddenApiLiteral.test(source)) {
      violations.push(`${posixPath}: direct /api/v1/ literal is not allowed outside api modules`);
    }
    if (forbiddenHttpImport.test(source)) {
      violations.push(`${posixPath}: direct import from api/http is not allowed outside api modules`);
    }
  }

  const manifestPath = path.join(root, 'contracts/openapi/vue-client-coverage-v1.json');
  const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
  const manifestModules = new Set((manifest.entries ?? []).map(entry => entry.apiModule));
  const infrastructureModules = new Set(manifest.infrastructureModules ?? []);
  const consumerBindings = manifest.consumerModules ?? [];

  for (const apiModule of manifestModules) {
    const consumers = consumerBindings
      .filter(binding => binding.apiModule === apiModule)
      .flatMap(binding => binding.consumers ?? []);
    if (consumers.length === 0 && !infrastructureModules.has(apiModule)) {
      violations.push(`${apiModule}: missing consumerModules binding in vue-client-coverage manifest`);
    }
  }

  for (const binding of consumerBindings) {
    for (const consumer of binding.consumers ?? []) {
      try {
        await stat(path.join(root, consumer));
      } catch {
        violations.push(`${binding.apiModule}: consumer module missing at ${consumer}`);
      }
    }
  }

  return violations;
}

async function main() {
  const violations = await validateVueApiCallSiteCoverage();
  if (violations.length > 0) {
    console.error('Vue API call-site coverage validation failed:');
    for (const violation of violations) {
      console.error(`- ${violation}`);
    }
    process.exitCode = 1;
    return;
  }

  console.log('Vue API call-site coverage validation passed.');
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  main().catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
}
