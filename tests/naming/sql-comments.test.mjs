import assert from 'node:assert/strict';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { describeColumn, describeTable } from '../../scripts/database/object-comment-catalog.mjs';
import { generateObjectCommentsCatalog } from '../../scripts/database/generate-object-comments.mjs';
import {
  validateCommentCatalogCoverage,
  validateRepositorySqlComments,
  validateSqlComments,
} from '../../scripts/database/validate-sql-comments.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const fixtureRoot = path.join(repositoryRoot, 'tests/fixtures/naming');

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
  const first = generateObjectCommentsCatalog({ repositoryRoot });
  const second = generateObjectCommentsCatalog({ repositoryRoot });
  assert.equal(first.tableCount, second.tableCount);
  assert.ok(first.tableCount >= 60);
});
