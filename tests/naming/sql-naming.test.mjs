import assert from 'node:assert/strict';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import os from 'node:os';
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

test('明确的 ALTER TABLE DROP CONSTRAINT 可扫描，其他 DROP 仍报告', async () => {
  const violations = await validateSqlNaming(
    [path.join(fixtureRoot, 'drop-constraint.sql')],
    { repositoryRoot, debt: { schemaVersion: 1, items: [] } }
  );

  assert.equal(violations.filter(item => item.ruleId === 'FNSQL003').length, 1);
  assert.equal(violations.find(item => item.ruleId === 'FNSQL003')?.line, 2);
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

test('双库静态删索引仍检查表和长度，缺少目标、复合、未知 Schema 及动态语句拒绝', async () => {
  const violations = await validateSqlNaming([path.join(fixtureRoot, 'drop-index.sql')],
    { repositoryRoot, debt: { schemaVersion: 1, items: [] } });
  assert.deepEqual(violations.filter(item => item.ruleId === 'FNSQL003').map(item => item.line), [4, 5, 6, 7]);
  assert.ok(violations.some(item => item.ruleId === 'FNDB001' && item.line === 3 && item.actual === 'BadTarget'));
  assert.ok(violations.some(item => item.ruleId === 'FNDB004' && item.line === 8));
  assert.ok(violations.some(item => item.ruleId === 'FNSQL002' && item.line === 9));
  assert.ok(violations.every(item => item.line > 2));
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

test('触发器 BEFORE UPDATE 不把 ON 误判为业务表', async () => {
  const violations = await validateSqlNaming(
    [path.join(fixtureRoot, 'trigger-update.sql')],
    { repositoryRoot, debt: { schemaVersion: 1, items: [] } }
  );

  assert.equal(violations.some(item => item.ruleId === 'FNDB001' && item.actual === 'ON'), false);
  assert.equal(violations.some(item => item.ruleId === 'FNSQL003'), true);
});

test('MySQL 表选项结束建表块且不把存储过程变量识别为列', async () => {
  const violations = await validateSqlNaming(
    [path.join(fixtureRoot, 'mysql-table-options.sql')],
    { repositoryRoot, debt: { schemaVersion: 1, items: [] } }
  );

  assert.equal(violations.some(item => item.ruleId === 'FNDB002'), false);
  assert.equal(violations.some(item => item.ruleId === 'FNSQL003'), true);
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

test('单表静态重命名检查源与目标命名，复合或动态语句仍拒绝', async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), 'fullnet-naming-rename-'));
  const file = path.join(directory, 'rename.sql');
  const options = { repositoryRoot, debt: { schemaVersion: 1, items: [] } };
  try {
    await writeFile(file, 'RENAME TABLE fn_ai_old_manifest TO fn_ai_document_manifest;');
    assert.deepEqual(await validateSqlNaming([file], options), []);
    for (const sql of ['RENAME TABLE BadSource TO fn_ai_document_manifest;', 'RENAME TABLE fn_ai_old_manifest TO BadTarget;']) {
      await writeFile(file, sql);
      assert.ok((await validateSqlNaming([file], options)).some(item => item.ruleId === 'FNDB001'));
    }
    for (const sql of ['RENAME TABLE fn_ai_old_manifest TO fn_ai_document_manifest, fn_ai_other TO fn_ai_third;', "PREPARE p FROM 'RENAME TABLE fn_ai_old_manifest TO fn_ai_document_manifest';"]) {
      await writeFile(file, sql);
      assert.ok((await validateSqlNaming([file], options)).some(item => ['FNSQL002', 'FNSQL003'].includes(item.ruleId)));
    }
  } finally { await rm(directory, { recursive: true }); }
});


test('静态恢复过程可识别，过程体的危险查询和不完整清理仍失败', async () => {
  const fixture = path.join(fixtureRoot, 'static-recovery-procedure.sql');
  const options = { repositoryRoot, debt: { schemaVersion: 1, items: [] } };
  assert.deepEqual(await validateSqlNaming([fixture], options), []);
  const temporary = await mkdtemp(path.join(os.tmpdir(), 'fullnet-naming-recovery-'));
  try {
    const valid = await readFile(fixture, 'utf8');
    const unsafe = path.join(temporary, 'unsafe.sql');
    await writeFile(unsafe, valid.replace('    IF NOT EXISTS', '    SELECT * FROM fn_settings_config_entry;\n    IF NOT EXISTS'));
    assert.ok((await validateSqlNaming([unsafe], options)).some(item => item.ruleId === 'FNSQL001'));
    await writeFile(unsafe, valid.replace('CALL fn_settings_trace_recovery();', 'CALL fn_settings_other();'));
    assert.ok((await validateSqlNaming([unsafe], options)).some(item => item.ruleId === 'FNSQL003'));
    await writeFile(unsafe, valid.replace('    IF NOT EXISTS', "    PREPARE p FROM @ddl;\n    IF NOT EXISTS"));
    assert.ok((await validateSqlNaming([unsafe], options)).some(item => item.ruleId === 'FNSQL002'));
  } finally { await rm(temporary, { recursive: true, force: true }); }
});
