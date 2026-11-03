import assert from 'node:assert/strict';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { validateSqlNaming } from '../../scripts/naming/validate-sql-names.mjs';
import { validateUuidStorageSql } from '../../scripts/naming/validate-uuid-storage-sql.mjs';
import { validateSqlSafety } from '../../scripts/sql/validate-sql-safety.mjs';
import { validateSqlComments } from '../../scripts/database/validate-sql-comments.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const files = ['SqlServer', 'MySql'].flatMap(provider =>
  ['289_DocumentPreviewLifecycle.sql'].map(name =>
    path.join(repositoryRoot, 'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations', provider, name)));

// 只验收第三批两份迁移；真实非空数据映射及未记账恢复由双库 Integration 覆盖。
for (const [name, validate] of Object.entries({ naming: validateSqlNaming, uuid: validateUuidStorageSql,
  safety: validateSqlSafety, comments: validateSqlComments })) {
  test(`Document 第三批迁移通过 ${name} 门禁`, async () => {
    assert.deepEqual(await validate(files, { repositoryRoot }), []);
  });
}
