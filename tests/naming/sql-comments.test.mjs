import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { describeColumn, describeTable } from '../../scripts/database/object-comment-catalog.mjs';
import { extractSchemaFromMigrations, generateObjectCommentsCatalog } from '../../scripts/database/generate-object-comments.mjs';
import {
  validateCommentCatalogCoverage,
  validateRepositorySqlComments,
  validateSqlComments,
} from '../../scripts/database/validate-sql-comments.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const fixtureRoot = path.join(repositoryRoot, 'tests/fixtures/naming');

test('收费增量授权注释保留原会话和不返还额度的约束', () => {
  assert.equal(describeTable('fn_ai_index_grant'), '所有者原会话的有界增量收费授权；默认关闭且不可接管');
  assert.equal(describeColumn('fn_ai_index_grant', 'SessionJson'), '冻结原交互会话绑定；不包含访问令牌且不可替换');
  assert.equal(describeColumn('fn_ai_index_grant_item', 'ReservedInputTokens'), '本任务已消耗的输入预留总量；不因未知或失败返还');
});

test('MySQL 幂等建表与 ENGINE 子句中的列进入注释目录', () => {
  const schema = extractSchemaFromMigrations(path.join(fixtureRoot, 'comment-extraction'));
  assert.deepEqual(Object.keys(schema).sort(), [
    'fn_identity_fixture_application',
    'fn_identity_fixture_session',
    'fn_identity_fixture_token',
  ]);
  assert.deepEqual([...schema.fn_identity_fixture_application], ['Id', 'ClientId']);
  assert.deepEqual([...schema.fn_identity_fixture_session], ['Id', 'ActiveSessionKey']);
  assert.deepEqual([...schema.fn_identity_fixture_token], ['Id', 'Payload']);
});

test('注释目录生成器覆盖全部迁移表', () => {
  const violations = validateCommentCatalogCoverage({ repositoryRoot });
  assert.deepEqual(violations, []);
});

test('注释生成器为常见列提供中文语义', () => {
  assert.equal(describeTable('fn_identity_user'), '身份认证用户表');
  assert.equal(describeColumn('fn_identity_user', 'TenantId'), '租户标识；NULL 表示 Host 级');
  assert.equal(describeColumn('fn_identity_user', 'CreatedAtUtc'), '创建时间(UTC)');
});

test('带注释的夹具不产生违规', async () => {
  const violations = await validateSqlComments(
    [path.join(fixtureRoot, 'commented-schema.sql')],
    { repositoryRoot }
  );
  assert.deepEqual(violations, []);
});

test('缺少注释的夹具报告表与列违规', async () => {
  const violations = await validateSqlComments(
    [path.join(fixtureRoot, 'valid-schema.sql')],
    { repositoryRoot }
  );
  const rules = new Set(violations.map(item => item.ruleId));
  assert.ok(rules.has('FNDBC002'));
  assert.ok(rules.has('FNDBC004'));
});

test('仓库迁移脚本全部包含数据库对象注释', async () => {
  const violations = await validateRepositorySqlComments(repositoryRoot);
  assert.deepEqual(violations, []);
});

test('generate-object-comments 可重复生成目录', () => {
  // 验证输出可重现，但不能覆盖共享工作区中已有的正式目录或其他会话改动。
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'fullnet-object-comments-'));
  const outputPath = path.join(temporary, 'catalog.json');
  try {
    const first = generateObjectCommentsCatalog({ repositoryRoot, outputPath });
    const bytes = fs.readFileSync(outputPath);
    const second = generateObjectCommentsCatalog({ repositoryRoot, outputPath });
    assert.equal(first.tableCount, second.tableCount);
    assert.equal(first.outputPath, outputPath);
    assert.deepEqual(bytes, fs.readFileSync(outputPath));
    assert.ok(first.tableCount >= 60);
  } finally {
    if (fs.existsSync(outputPath)) fs.unlinkSync(outputPath);
    fs.rmdirSync(temporary);
  }
});
