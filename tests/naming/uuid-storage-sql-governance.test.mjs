import assert from 'node:assert/strict';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  validateRepositoryUuidStorageSql,
  validateUuidStorageSql
} from '../../scripts/naming/validate-uuid-storage-sql.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const fixtureGovernanceRoot = path.join(repositoryRoot, 'tests/fixtures/naming/uuid-governance');
const productionMigrationRoot = path.join(
  repositoryRoot,
  'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations'
);

test('010+ MySQL 迁移禁止 UUID 列使用 char(36)', async () => {
  const violations = await validateUuidStorageSql(
    [path.join(fixtureGovernanceRoot, 'MySql/010_InvalidUuidLegacy.sql')],
    { repositoryRoot }
  );

  assert.equal(violations.length, 2);
  assert.ok(violations.every(item => item.ruleId === 'FNUUID001'));
});

test('010+ SQL Server 迁移要求 UUID 主键显式聚集属性', async () => {
  const violations = await validateUuidStorageSql(
    [path.join(fixtureGovernanceRoot, 'SqlServer/010_InvalidUuidCluster.sql')],
    { repositoryRoot }
  );

  const rules = new Set(violations.map(item => item.ruleId));
  assert.deepEqual(rules, new Set(['FNUUID002', 'FNUUID003']));
});

test('001-009 历史与过渡迁移不受 010+ UUID 门禁约束', async () => {
  const violations = await validateUuidStorageSql([
    path.join(productionMigrationRoot, 'MySql/007_SeedExecutionAudit.sql'),
    path.join(productionMigrationRoot, 'MySql/008_UuidBinaryExpand.sql'),
    path.join(productionMigrationRoot, 'MySql/009_UuidBinaryContract.sql'),
    path.join(productionMigrationRoot, 'SqlServer/009_UuidBinaryContract.sql')
  ], { repositoryRoot });

  assert.deepEqual(violations, []);
});

test('仓库现有迁移满足 UUID 存储 SQL 门禁', async () => {
  const violations = await validateRepositoryUuidStorageSql(repositoryRoot);
  assert.deepEqual(violations, []);
});
