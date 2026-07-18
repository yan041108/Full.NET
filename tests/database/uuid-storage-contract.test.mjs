import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const migrationRoot = path.join(
  repositoryRoot,
  'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations'
);
const contractPath = path.join(repositoryRoot, 'contracts/database/uuid-storage-v1.json');
const requiredColumnFields = [
  'table',
  'column',
  'nullable',
  'role',
  'referencedTable',
  'referencedColumn',
  'sqlServerType',
  'mySqlLegacyType',
  'mySqlTargetType'
];
const allowedRoles = new Set([
  'primary',
  'foreign',
  'reference',
  'lease',
  'family'
]);
const expectedReferenceTargets = new Map(Object.entries({
  'fn_outbox_message.TenantId': 'fn_tenant_tenant.Id',
  'fn_identity_user.TenantId': 'fn_tenant_tenant.Id',
  'fn_identity_refresh_session.UserId': 'fn_identity_user.Id',
  'fn_identity_refresh_session.ReplacedById': 'fn_identity_refresh_session.Id',
  'fn_identity_refresh_session.ActiveTenantId': 'fn_tenant_tenant.Id',
  'fn_identity_auth_audit.UserId': 'fn_identity_user.Id',
  'fn_identity_auth_audit.SessionId': 'fn_identity_refresh_session.Id',
  'fn_identity_auth_audit.ContextTenantId': 'fn_tenant_tenant.Id',
  'fn_identity_auth_audit.ActorUserId': 'fn_identity_user.Id',
  'fn_identity_role.TenantId': 'fn_tenant_tenant.Id',
  'fn_identity_user_role.UserId': 'fn_identity_user.Id',
  'fn_identity_user_role.RoleId': 'fn_identity_role.Id',
  'fn_identity_role_permission.RoleId': 'fn_identity_role.Id',
  'fn_seed_run_item.RunId': 'fn_seed_run.Id'
}));

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

async function readProviderMigrations(provider) {
  const directory = path.join(migrationRoot, provider);
  const files = (await readdir(directory))
    .filter(file => /^00[1-7]_.*\.sql$/u.test(file))
    .sort();
  return (await Promise.all(files.map(async file => ({
    file,
    sql: await readFile(path.join(directory, file), 'utf8')
  }))));
}

function addColumn(columns, table, column, type, nullability, source) {
  const key = `${table}.${column}`;
  assert.ok(!columns.has(key), `重复扫描到 UUID 列 ${key}（${source}）`);
  columns.set(key, {
    table,
    column,
    nullable: nullability.toUpperCase() === 'NULL',
    type: type.toLowerCase()
  });
}

function scanProvider(migrations) {
  const columns = new Map();
  const foreignKeys = [];
  for (const { file, sql } of migrations) {
    const createPattern = /CREATE\s+TABLE(?:\s+IF\s+NOT\s+EXISTS)?\s+(?:dbo\.)?([A-Za-z][A-Za-z0-9_]*)\s*\(([\s\S]*?)\)\s*(?:ENGINE\s*=\s*[^;]+)?;/giu;
    for (const create of sql.matchAll(createPattern)) {
      const [, table, body] = create;
      const columnPattern = /^\s*([A-Za-z][A-Za-z0-9_]*)\s+(uniqueidentifier|char\(36\))\s+(NOT\s+NULL|NULL)\b/gimu;
      for (const column of body.matchAll(columnPattern)) {
        addColumn(columns, table, column[1], column[2], column[3], file);
      }

      const foreignKeyPattern = /FOREIGN\s+KEY\s*\(\s*([A-Za-z][A-Za-z0-9_]*)\s*\)\s+REFERENCES\s+(?:dbo\.)?([A-Za-z][A-Za-z0-9_]*)\s*\(\s*([A-Za-z][A-Za-z0-9_]*)\s*\)/giu;
      for (const foreignKey of body.matchAll(foreignKeyPattern)) {
        foreignKeys.push({
          table,
          column: foreignKey[1],
          referencedTable: foreignKey[2],
          referencedColumn: foreignKey[3]
        });
      }
    }

    const alterPattern = /ALTER\s+TABLE\s+(?:dbo\.)?([A-Za-z][A-Za-z0-9_]*)\s+ADD(?:\s+COLUMN)?\s+([A-Za-z][A-Za-z0-9_]*)\s+(uniqueidentifier|char\(36\))\s+(NOT\s+NULL|NULL)\b/giu;
    for (const alter of sql.matchAll(alterPattern)) {
      addColumn(columns, alter[1], alter[2], alter[3], alter[4], file);
    }
  }

  return { columns, foreignKeys };
}

function contractColumnMap(contract) {
  const result = new Map();
  for (const column of contract.columns) {
    for (const field of requiredColumnFields) {
      assert.ok(Object.hasOwn(column, field), `合同列缺少字段 ${field}`);
    }

    const key = `${column.table}.${column.column}`;
    assert.ok(!result.has(key), `合同重复登记 ${key}`);
    assert.ok(allowedRoles.has(column.role), `${key} 使用未知角色 ${column.role}`);
    assert.equal(column.sqlServerType, 'uniqueidentifier', `${key} SQL Server 类型错误`);
    assert.equal(column.mySqlLegacyType, 'char(36)', `${key} MySQL legacy 类型错误`);
    assert.equal(column.mySqlTargetType, 'binary(16)', `${key} MySQL 目标类型错误`);
    result.set(key, column);
  }

  return result;
}

function canonicalKeys(map) {
  return [...map.keys()].sort();
}

function timeSwapHex(uuid) {
  const [timeLow, timeMid, timeHigh, clockSequence, node] = uuid.split('-');
  return `${timeHigh}${timeMid}${timeLow}${clockSequence}${node}`;
}

test('UUID 存储合同完整覆盖 001-007 双库列与引用关系', async () => {
  const contract = await loadContract();
  const sqlServer = scanProvider(await readProviderMigrations('SqlServer'));
  const mySql = scanProvider(await readProviderMigrations('MySql'));
  const registered = contractColumnMap(contract);

  assert.equal(contract.contract, 'UuidStorageContractV1');
  assert.equal(contract.schemaVersion, 1);
  assert.equal(contract.columns.length, 23);
  assert.deepEqual(canonicalKeys(sqlServer.columns), canonicalKeys(mySql.columns));
  assert.deepEqual(canonicalKeys(registered), canonicalKeys(sqlServer.columns));

  for (const [key, column] of registered) {
    const sqlServerColumn = sqlServer.columns.get(key);
    const mySqlColumn = mySql.columns.get(key);
    assert.equal(column.nullable, sqlServerColumn.nullable, `${key} SQL Server 可空性漂移`);
    assert.equal(column.nullable, mySqlColumn.nullable, `${key} MySQL 可空性漂移`);
    assert.equal(sqlServerColumn.type, column.sqlServerType, `${key} SQL Server 类型漂移`);
    assert.equal(mySqlColumn.type, column.mySqlLegacyType, `${key} MySQL 类型漂移`);

    if (column.referencedTable === null || column.referencedColumn === null) {
      assert.equal(column.referencedTable, null, `${key} 引用表/列必须同时为空`);
      assert.equal(column.referencedColumn, null, `${key} 引用表/列必须同时为空`);
    } else {
      assert.ok(
        registered.has(`${column.referencedTable}.${column.referencedColumn}`),
        `${key} 引用了未登记 UUID 列`
      );
    }
  }

  const expectedForeignKeys = sqlServer.foreignKeys
    .map(item => JSON.stringify(item))
    .sort();
  assert.deepEqual(
    mySql.foreignKeys.map(item => JSON.stringify(item)).sort(),
    expectedForeignKeys
  );
  const registeredForeignKeys = [...registered.values()]
    .filter(column => column.role === 'foreign')
    .map(column => JSON.stringify({
      table: column.table,
      column: column.column,
      referencedTable: column.referencedTable,
      referencedColumn: column.referencedColumn
    }))
    .sort();
  assert.deepEqual(registeredForeignKeys, expectedForeignKeys);

  const registeredReferenceTargets = new Map(
    [...registered.entries()]
      .filter(([, column]) => column.referencedTable !== null)
      .map(([key, column]) => [
        key,
        `${column.referencedTable}.${column.referencedColumn}`
      ])
  );
  assert.deepEqual(
    [...registeredReferenceTargets.entries()].sort(),
    [...expectedReferenceTargets.entries()].sort(),
    '合同中的 UUID 语义引用目标发生漂移'
  );
});

test('UUID v7 固定向量使用 RFC 9562 网络字节序且拒绝 time-swap', async () => {
  const contract = await loadContract();

  assert.equal(contract.mySql.byteOrder, 'rfc-9562-network');
  assert.equal(contract.mySql.guidFormat, 'Binary16');
  assert.equal(contract.mySql.uuidToBinSwapFlag, 0);
  assert.ok(contract.vectors.length >= 3);
  for (const vector of contract.vectors) {
    assert.match(
      vector.uuid,
      /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u
    );
    const expectedHex = vector.uuid.replaceAll('-', '');
    assert.equal(vector.hex, expectedHex);
    assert.equal(Buffer.from(vector.hex, 'hex').length, 16);
    assert.notEqual(timeSwapHex(vector.uuid), vector.hex);
  }
});

test('合同冻结 008/009 存储迁移并为 010/011 命名迁移保留顺序', async () => {
  const contract = await loadContract();

  assert.deepEqual(contract.storageMigrations, [
    '008_UuidBinaryExpand',
    '009_UuidBinaryContract'
  ]);
  assert.deepEqual(contract.followingNamingMigrations, [
    '010_NamingExpand',
    '011_NamingContract'
  ]);
  assert.equal(contract.legacyMigrationRange, '001-007');
});
