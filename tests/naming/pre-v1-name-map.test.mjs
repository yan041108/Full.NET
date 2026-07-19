import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { buildDatabaseObjectName } from '../../scripts/naming/database-object-name.mjs';
import { loadNamingDebt, loadNamingProfile } from '../../scripts/naming/load-naming-profile.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const mapPath = path.join(repositoryRoot, 'contracts/naming/pre-v1-name-map.json');

const requiredDatabaseMappings = [
  ['fn_tenant_tenant', 'fn_tenancy_tenant'],
  ['fn_tenant_tenant.CreatedAt', 'fn_tenancy_tenant.CreatedAtUtc'],
  ['fn_tenant_tenant.UpdatedAt', 'fn_tenancy_tenant.UpdatedAtUtc'],
  ['fn_outbox_message.Type', 'fn_outbox_message.MessageType'],
  ['fn_outbox_message.OccurredAt', 'fn_outbox_message.OccurredAtUtc'],
  ['fn_outbox_message.ProcessedAt', 'fn_outbox_message.ProcessedAtUtc'],
  ['fn_outbox_message.NextAttemptAt', 'fn_outbox_message.NextAttemptAtUtc'],
  ['fn_outbox_message.LockedUntil', 'fn_outbox_message.LockedUntilUtc']
];

const requiredProtocolMappings = [
  ['message_type', 'fullnet.tenancy.tenant-provisioned', 'fullnet.tenancy.tenant.provisioned'],
  ['error_code', 'identity.bootstrap.invalid-password', 'identity.bootstrap.invalid_password'],
  ['error_code', 'tenancy.domain-exists', 'tenancy.domain_exists'],
  ['error_code', 'identity.login-succeeded', 'identity.login_succeeded'],
  ['statement_id', 'outbox.acquire.sql-server', 'outbox.acquire.sql_server']
];

async function loadMap() {
  return JSON.parse(await readFile(mapPath, 'utf8'));
}

function canonicalizeProtocolValue(kind, legacyValue) {
  if (kind === 'message_type' && legacyValue === 'fullnet.tenancy.tenant-provisioned') {
    return 'fullnet.tenancy.tenant.provisioned';
  }

  return legacyValue
    .split('.')
    .map(segment => segment.replaceAll('-', '_'))
    .join('.');
}

test('PreV1NameMapV1 冻结 010/011 与最小数据库/协议映射', async () => {
  const map = await loadMap();
  const profile = await loadNamingProfile(repositoryRoot);

  assert.equal(map.contract, 'PreV1NameMapV1');
  assert.equal(map.schemaVersion, 1);
  assert.deepEqual(map.migrations, {
    expand: '010_NamingExpand',
    contract: '011_NamingContract'
  });

  const tablePattern = new RegExp(profile.database.tablePattern);
  const columnPattern = new RegExp(profile.database.columnPattern);
  const tableByLegacy = new Map(map.database.tables.map(item => [item.legacyName, item]));
  const columnByLegacy = new Map(
    map.database.columns.map(item => [`${item.tableLegacy}.${item.legacyName}`, item])
  );

  for (const [legacy, canonical] of requiredDatabaseMappings) {
    if (legacy.includes('.')) {
      const entry = columnByLegacy.get(legacy);
      assert.ok(entry, `缺少列映射：${legacy}`);
      const expectedTable = legacy.startsWith('fn_tenant_tenant.')
        ? 'fn_tenancy_tenant'
        : entry.tableLegacy;
      assert.equal(`${expectedTable}.${entry.canonicalName}`, canonical);
      assert.equal(entry.expandName, entry.canonicalName);
      assert.ok(entry.switchRelease);
      assert.ok(entry.contractRelease);
      continue;
    }

    const entry = tableByLegacy.get(legacy);
    assert.ok(entry, `缺少表映射：${legacy}`);
    assert.equal(entry.canonicalName, canonical);
    assert.equal(entry.expandName, canonical);
    assert.ok(entry.switchRelease);
    assert.ok(entry.contractRelease);
  }

  for (const table of map.database.tables) {
    assert.match(table.canonicalName, tablePattern);
    assert.ok(table.canonicalName.length <= profile.database.maxIdentifierLength);
    assert.ok(table.expandName.length <= profile.database.maxIdentifierLength);
  }

  for (const column of map.database.columns) {
    assert.match(column.canonicalName, columnPattern);
    const indexName = buildDatabaseObjectName(`IX_${column.tableLegacy}_${column.canonicalName}`);
    assert.ok(indexName.length <= profile.database.maxIdentifierLength);
  }

  const protocolByLegacy = new Map(
    map.protocol.map(item => [`${item.kind}:${item.legacyValue}`, item])
  );
  for (const [kind, legacy, canonical] of requiredProtocolMappings) {
    const entry = protocolByLegacy.get(`${kind}:${legacy}`);
    assert.ok(entry, `缺少协议映射：${kind} ${legacy}`);
    assert.equal(entry.canonicalValue, canonical);
    assert.ok(entry.compatibilityMode, `${legacy} 缺少 compatibilityMode`);
  }

  const canonicalTargets = new Set();
  for (const item of map.protocol) {
    assert.equal(item.canonicalValue, canonicalizeProtocolValue(item.kind, item.legacyValue));
    assert.ok(item.compatibilityMode);
    const key = `${item.kind}:${item.canonicalValue}`;
    assert.ok(!canonicalTargets.has(key), `协议目标重复：${key}`);
    canonicalTargets.add(key);
  }
});

test('命名债务中的连字符协议值在 PreV1NameMapV1 中有唯一目标', async () => {
  const map = await loadMap();
  const debt = await loadNamingDebt(repositoryRoot);
  const protocolKinds = new Set(['error_code', 'message_type', 'statement_id']);
  const protocolByLegacy = new Map(
    map.protocol.map(item => [`${item.kind}:${item.legacyValue}`, item])
  );

  const hyphenDebts = [...new Set(
    debt.items
      .filter(item => protocolKinds.has(item.kind) && item.value.includes('-'))
      .map(item => `${item.kind}:${item.value}`)
  )].sort();

  assert.ok(hyphenDebts.length > 0);
  for (const key of hyphenDebts) {
    assert.ok(protocolByLegacy.has(key), `债务未登记协议映射：${key}`);
  }

  assert.ok(protocolByLegacy.has('error_code:identity.login-succeeded'));
});
