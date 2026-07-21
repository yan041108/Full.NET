import assert from 'node:assert/strict';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  loadSqlSafetyWaivers,
  validateRepositorySqlSafety,
  validateSqlSafety
} from '../../scripts/sql/validate-sql-safety.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const fixtureRoot = path.join(repositoryRoot, 'tests/fixtures/sql-safety');

test('规范写操作与非破坏性 DDL 不产生违规', async () => {
  const violations = await validateSqlSafety(
    [path.join(fixtureRoot, 'valid-writes.sql')],
    { repositoryRoot, waivers: { schemaVersion: 1, items: [] } }
  );
  assert.deepEqual(violations, []);
});

test('SQL 安全门禁报告无 WHERE、TRUNCATE、DROP 与 RENAME', async () => {
  const violations = await validateSqlSafety(
    [path.join(fixtureRoot, 'invalid-writes.sql')],
    { repositoryRoot, waivers: { schemaVersion: 1, items: [] } }
  );
  const rules = new Set(violations.map(item => item.ruleId));
  assert.deepEqual(
    rules,
    new Set(['FNSAFETY001', 'FNSAFETY002', 'FNSAFETY003', 'FNSAFETY004'])
  );
  assert.ok(violations.some(item => item.actual === 'update_without_where'));
  assert.ok(violations.some(item => item.actual === 'delete_without_where'));
  assert.ok(violations.some(item => item.actual === 'DROP TABLE'));
  assert.ok(violations.some(item => item.actual === 'DROP COLUMN'));
  assert.ok(violations.every(item => item.file === 'tests/fixtures/sql-safety/invalid-writes.sql'));
  assert.ok(violations.every(item => Number.isInteger(item.line) && item.line > 0));
});

test('豁免只能精确放行同文件、同行号、同 actual', async () => {
  const file = path.join(fixtureRoot, 'invalid-writes.sql');
  const exact = {
    schemaVersion: 1,
    items: [{
      ruleId: 'FNSAFETY002',
      file: 'tests/fixtures/sql-safety/invalid-writes.sql',
      line: 7,
      actual: 'TRUNCATE TABLE',
      reason: 'fixture exact waiver',
      risk: 'none',
      backupVerified: true,
      reviewer: 'tests',
      removalMilestone: 'fixture'
    }]
  };
  const remaining = await validateSqlSafety([file], { repositoryRoot, waivers: exact });
  assert.equal(remaining.some(item => item.ruleId === 'FNSAFETY002'), false);
  assert.ok(remaining.some(item => item.ruleId === 'FNSAFETY001'));

  const wrongLine = structuredClone(exact);
  wrongLine.items[0].line = 99;
  const withWrongLine = await validateSqlSafety([file], {
    repositoryRoot,
    waivers: wrongLine
  });
  assert.equal(withWrongLine.some(item => item.ruleId === 'FNSAFETY002'), true);
});

test('豁免文档拒绝 backupVerified=false', async () => {
  await assert.rejects(
    () => loadSqlSafetyWaivers(path.join(fixtureRoot, 'broken-waiver-root')),
    /backupVerified/);
});

test('仓库生产 SQL 满足安全门禁或精确豁免', async () => {
  const violations = await validateRepositorySqlSafety(repositoryRoot);
  assert.deepEqual(violations, []);
});
