import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

function parseDebtCatalog(relativePath) {
  return read(relativePath).then(content => JSON.parse(content));
}

const architectureDebtFiles = [
  'contracts/architecture/module-local-transaction-debt.json',
  'contracts/architecture/module-table-access-debt.json',
  'contracts/architecture/module-cross-foreign-key-debt.json'
];

const forbiddenPhrasesWhenDebtEmpty = [
  '跨模块本地事务债务',
  '精确反向模块契约债务',
  '精确模块契约债务',
  '反向模块契约债务'
];

test('architecture 债务目录为空时 capability-status 不得声称仍存登记债务', async () => {
  const catalogs = await Promise.all(
    architectureDebtFiles.map(async filePath => {
      const catalog = await parseDebtCatalog(filePath);
      return { filePath, entries: catalog.entries ?? [] };
    })
  );

  for (const { filePath, entries } of catalogs) {
    assert.deepEqual(
      entries,
      [],
      `${filePath} 必须为空，或同步更新 capability-status 与治理测试`
    );
  }

  const capabilityStatus = await read('docs/roadmap/capability-status.md');
  for (const phrase of forbiddenPhrasesWhenDebtEmpty) {
    assert.doesNotMatch(
      capabilityStatus,
      new RegExp(phrase),
      `capability-status 在债务目录已空时不得包含“${phrase}”`
    );
  }
});

test('AllowedReverseContractDependencies 为空时文档不得声称反向契约债务仍存在', async () => {
  const dependencyRules = await read(
    'tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs'
  );
  assert.match(
    dependencyRules,
    /AllowedReverseContractDependencies\s*=\s*\[\s*\]/
  );

  const capabilityStatus = await read('docs/roadmap/capability-status.md');
  assert.doesNotMatch(
    capabilityStatus,
    /反向.*契约债务|精确.*模块契约债务/
  );
});

test('Grid Preference 与 FusionCache 缓存 allowlist 表述不得矛盾', async () => {
  const capabilityStatus = await read('docs/roadmap/capability-status.md');

  assert.match(
    capabilityStatus,
    /FusionCache 多实例缓存治理[\s\S]*?Architecture 手工策略 allowlist 为零/
  );
  assert.doesNotMatch(
    capabilityStatus,
    /Grid Preference[\s\S]*?仍需从 Architecture allowlist 迁入/
  );
  assert.match(
    capabilityStatus,
    /Grid Preference[\s\S]*?allowlist 为零/
  );
});
