import assert from 'node:assert/strict';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  validateMigrationPairs,
  validateRepositorySqlNaming,
  validateSqlNaming
} from '../../scripts/naming/validate-sql-names.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const fixtureRoot = path.join(repositoryRoot, 'tests/fixtures/naming');

test('规范 DDL 和显式查询不产生违规', async () => {
  const violations = await validateSqlNaming(
    [path.join(fixtureRoot, 'valid-schema.sql')],
    { repositoryRoot }
  );

  assert.deepEqual(violations, []);
});

test('SQL 门禁报告表、列、约束、长度、保留所有权和 SELECT 星号', async () => {
  const violations = await validateSqlNaming(
    [path.join(fixtureRoot, 'invalid-schema.sql')],
    { repositoryRoot }
  );
  const rules = new Set(violations.map(item => item.ruleId));

  assert.deepEqual(
    rules,
    new Set([
      'FNDB001',
      'FNDB002',
      'FNDB003',
      'FNDB004',
      'FNDB005',
      'FNSQL001',
      'FNSQL002',
      'FNSQL003'
    ])
  );
  assert.ok(violations.every(item => item.file === 'tests/fixtures/naming/invalid-schema.sql'));
  assert.ok(violations.every(item => Number.isInteger(item.line) && item.line > 0));
  assert.ok(violations.every(item => item.actual && item.recommendation));
});

test('债务清单只能精确放行同文件、同类型和同值', async () => {
  const file = path.join(fixtureRoot, 'debt-schema.sql');
  const exactDebt = {
    schemaVersion: 1,
    items: [{
      kind: 'table',
      value: 'fn_tenant_tenant',
      file: 'tests/fixtures/naming/debt-schema.sql',
      reason: '测试精确债务。',
      removalMilestone: 'M1.0'
    }]
  };

  assert.deepEqual(await validateSqlNaming([file], { repositoryRoot, debt: exactDebt }), []);
  const wrongFile = structuredClone(exactDebt);
  wrongFile.items[0].file = 'tests/fixtures/naming/other.sql';
  assert.equal((await validateSqlNaming([file], { repositoryRoot, debt: wrongFile })).length, 1);
});

test('SQL Server 和 MySQL 迁移必须按文件名成对出现', async () => {
  const migrationRoot = path.join(fixtureRoot, 'Migrations');
  const violations = await validateMigrationPairs(migrationRoot, { repositoryRoot });

  assert.equal(violations.length, 1);
  assert.equal(violations[0].ruleId, 'FNMIG001');
  assert.equal(violations[0].actual, '008_MissingPeer.sql');
  assert.match(violations[0].recommendation, /MySql/);
});

test('仓库 SQL 与登记的 C# 静态 SQL 不存在未登记命名债务', async () => {
  const violations = await validateRepositorySqlNaming(repositoryRoot);

  assert.deepEqual(violations, []);
});
