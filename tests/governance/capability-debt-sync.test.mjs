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

test('architecture 债务目录与 capability-status 表述一致', async () => {
  const catalogs = await Promise.all(
    architectureDebtFiles.map(async filePath => {
      const catalog = await parseDebtCatalog(filePath);
      return { filePath, entries: catalog.entries ?? [] };
    })
  );

  const capabilityStatus = await read('docs/roadmap/capability-status.md');
  const localTransactionDebt = catalogs.find(
    ({ filePath }) => filePath.endsWith('module-local-transaction-debt.json')
  );
  const localTransactionEntries = localTransactionDebt?.entries ?? [];

  const catalogDocumentationPatterns = {
    'module-local-transaction-debt.json': /AcceptTenantInvitation|local-transaction/,
    'module-table-access-debt.json': /table-access|SessionBindingKinds/,
    'module-cross-foreign-key-debt.json': /cross-foreign-key/
  };

  for (const { filePath, entries } of catalogs) {
    const baseName = path.basename(filePath);
    if (entries.length === 0) {
      continue;
    }
    const pattern = catalogDocumentationPatterns[baseName];
    assert.ok(pattern, `${filePath} 含债务条目但未配置 capability-status 校验规则`);
    assert.match(
      capabilityStatus,
      pattern,
      `capability-status 必须登记 ${baseName} 中的债务条目`
    );
  }

  if (localTransactionEntries.length === 0) {
    for (const phrase of forbiddenPhrasesWhenDebtEmpty) {
      assert.doesNotMatch(
        capabilityStatus,
        new RegExp(phrase),
        `capability-status 在 local-transaction 债务目录已空时不得包含“${phrase}”`
      );
    }
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
