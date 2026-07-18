import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function readJson(relativePath) {
  return JSON.parse(await readFile(path.join(repositoryRoot, relativePath), 'utf8'));
}

test('Naming Profile 固化跨工具共享的命名基线', async () => {
  const profile = await readJson('contracts/naming/fullnet-naming-profile.json');

  assert.equal(profile.schemaVersion, 1);
  assert.equal(profile.database.frameworkOwnerKey, 'fn');
  assert.equal(profile.database.maxIdentifierLength, 64);
  assert.equal(profile.database.columnCase, 'pascal');
  assert.equal(profile.database.tableTemplate, '{owner}_{module}_{entity}');
  assert.equal(profile.database.constraintDigest.algorithm, 'sha256');
  assert.equal(profile.database.constraintDigest.hexLength, 8);
  assert.deepEqual(
    [...profile.database.reservedOwnerKeys].sort(),
    ['dbo', 'fn', 'information_schema', 'mysql', 'performance_schema', 'sys'].sort()
  );
  assert.equal(profile.contracts.jsonCase, 'camel');
  assert.equal(profile.contracts.permission.pattern, '^[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*$');
  assert.equal(profile.contracts.error.pattern, '^[a-z][a-z0-9_]*(?:\\.[a-z][a-z0-9_]*){2,}$');
  assert.equal(profile.contracts.message.pattern, '^[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*$');
});

test('命名债务只允许精确、有限期且可审计的条目', async () => {
  const debt = await readJson('contracts/naming/naming-debt.json');

  assert.equal(debt.schemaVersion, 1);
  assert.ok(debt.items.length > 0);
  const identities = new Set();
  for (const item of debt.items) {
    assert.match(item.kind, /^[a-z][a-z0-9_]*$/);
    assert.equal(typeof item.value, 'string');
    assert.ok(item.value.length > 0);
    assert.equal(typeof item.file, 'string');
    assert.ok(item.file.length > 0);
    assert.equal(typeof item.reason, 'string');
    assert.ok(item.reason.length > 0);
    assert.match(item.removalMilestone, /^M[0-9]+(?:\.[0-9]+)*$/);
    assert.ok(!item.value.includes('*'));
    assert.ok(!item.value.startsWith('/') || !item.value.endsWith('/'));
    const identity = `${item.kind}\u0000${item.value}\u0000${item.file}`;
    assert.ok(!identities.has(identity), `重复债务条目：${identity}`);
    identities.add(identity);
  }
});
