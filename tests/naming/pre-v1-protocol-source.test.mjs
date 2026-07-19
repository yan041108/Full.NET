import assert from 'node:assert/strict';
import { readdir, readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const mapPath = path.join(repositoryRoot, 'contracts/naming/pre-v1-name-map.json');

const allowedLegacyFiles = new Set([
  'src/BuildingBlocks/Full.NET.Hosting/Api/PreV1ProtocolCompatibility.cs',
  'contracts/naming/pre-v1-name-map.json',
  'contracts/naming/naming-debt.json',
  'tests/naming/pre-v1-name-map.test.mjs',
  'tests/naming/pre-v1-protocol-source.test.mjs',
  'tests/Full.NET.UnitTests/Naming/PreV1ProtocolCompatibilityTests.cs',
  'tests/Full.NET.CompatibilityTests/AdminNetApiResultMapperTests.cs',
  'tests/Full.NET.UnitTests/Results/ErrorCompatibilityTests.cs',
  'packages/client-contracts/src/pre-v1-protocol.ts',
  'packages/client-contracts/tests/pre-v1-protocol.test.ts',
  'docs/development/pre-v1-contract-name-migration.md',
  'src/Modules/Full.NET.Modules.Tenancy/TenantProvisionedCacheInvalidationHandler.cs',
].map(item => path.normalize(item)));

const scanRoots = [
  'src',
  path.join('packages', 'client-contracts', 'src'),
  path.join('ui', 'admin', 'src'),
  path.join('ui', 'admin-layui', 'js'),
  path.join('clients', 'uniapp', 'src'),
];

const allowedExtensions = new Set(['.cs', '.ts', '.js', '.vue', '.mjs']);

async function loadLegacyValues() {
  const map = JSON.parse(await readFile(mapPath, 'utf8'));
  return [...new Set(map.protocol.map(item => item.legacyValue))];
}

async function walkFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === 'bin' || entry.name === 'obj' || entry.name === 'dist') {
        continue;
      }

      files.push(...await walkFiles(fullPath));
      continue;
    }

    if (allowedExtensions.has(path.extname(entry.name))) {
      files.push(fullPath);
    }
  }

  return files;
}

test('生产源码不再直接输出 PreV1NameMapV1 登记的 legacy 协议值', async () => {
  const legacyValues = await loadLegacyValues();
  const offenders = [];

  for (const root of scanRoots) {
    const absoluteRoot = path.join(repositoryRoot, root);
    try {
      await stat(absoluteRoot);
    } catch {
      continue;
    }

    for (const file of await walkFiles(absoluteRoot)) {
      const relative = path.normalize(path.relative(repositoryRoot, file));
      if (allowedLegacyFiles.has(relative)) {
        continue;
      }

      const content = await readFile(file, 'utf8');
      for (const legacyValue of legacyValues) {
        if (content.includes(legacyValue)) {
          offenders.push(`${relative}: ${legacyValue}`);
        }
      }
    }
  }

  assert.deepEqual(offenders.sort(), []);
});
