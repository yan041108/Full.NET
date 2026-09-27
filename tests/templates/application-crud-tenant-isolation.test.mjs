import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { verifyApplicationCrudTenantIsolation } from './support/application-crud-tenant-isolation.mjs';

const localTenantId = '01900000-0000-7000-8000-000000000010';
const otherTenantId = '01900000-0000-7000-8000-000000000020';
const local = { id: '01900000-0000-7000-8000-000000000011', tenantId: localTenantId, name: 'Isolation local product', version: '1' };
const other = { id: '01900000-0000-7000-8000-000000000021', tenantId: otherTenantId, name: 'Isolation other product', version: '1' };
const initialAccessToken = 'secret-initial-tenant-token';
const tokens = Array.from({ length: 6 }, (_, index) => `secret-rotated-token-${index}`);
const notFound = { status: 404, code: 'catalog.products.not_found' };
const ctx = (index, tenantId, identifier) => ({ accessToken: tokens[index], context: { tenantId, identifier, scope: tenantId ? `tenant:${tenantId.replaceAll('-', '')}` : 'host' } });
function response(body, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': status === 404 ? 'application/problem+json' : 'application/json' } });
}
function runner(calls, failure) {
  const entries = [
    [ctx(0, null, 'host')], [{ id: otherTenantId, identifier: 'isolation-probe' }, 201], [ctx(1, localTenantId, 'local')], [local, 201],
    [ctx(2, otherTenantId, 'isolation-probe')], [notFound, 404], [{ items: [] }], [notFound, 404], [notFound, 404], [other, 201], [{ items: [other] }],
    [ctx(3, localTenantId, 'local')], [local], [notFound, 404], [{ items: [local] }], [notFound, 404], [notFound, 404], [local], [local],
    [ctx(4, otherTenantId, 'isolation-probe')], [other], [other], [notFound, 404], [{ items: [] }],
    [ctx(5, localTenantId, 'local')], [notFound, 404], [{ items: [] }],
  ];
  return async (url, options) => {
    const index = calls.length;
    calls.push({ url, options });
    if (failure === 'wrong-context' && index === 4) return response(ctx(2, localTenantId, 'local'));
    if (failure === 'cross-read-leak' && index === 5) return response(local);
    if (failure === 'cross-list-leak' && index === 6) return response({ items: [local] });
    if (failure === 'cross-update-allowed' && index === 7) return response(local);
    if (failure === 'cross-delete-allowed' && index === 8) return response(local);
    if (failure === 'local-row-mutated' && index === 12) return response({ ...local, name: 'cross tenant update' });
    if (failure === 'reverse-read-leak' && index === 13) return response(other);
    if (failure === 'reverse-list-leak' && index === 14) return response({ items: [local, other] });
    if (failure === 'reverse-update-allowed' && index === 15) return response(other);
    if (failure === 'reverse-delete-allowed' && index === 16) return response(other);
    if (failure === 'other-row-mutated' && index === 20) return response({ ...other, version: '2' });
    if (failure === 'request-error' && index === 5) throw new Error('transport failed ' + tokens[2]);
    assert.ok(index < entries.length, 'unexpected request');
    return response(...entries[index]);
  };
}
async function fixture(action) {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-tenant-isolation-'));
  try { await action(join(root, 'result.json')); } finally { rmSync(root, { recursive: true, force: true }); }
}
test('verifies bidirectional tenant read/list/update/delete isolation using each rotated token', async () => fixture(async (logPath) => {
  const calls = [];
  assert.deepEqual(await verifyApplicationCrudTenantIsolation('http://example.test', { localTenantId, initialAccessToken, logPath, request: runner(calls) }), { businessRequests: 20, crossTenantNotFound: 6, ownRowsPreserved: 2, ownRowsDeleted: 2 });
  assert.equal(calls.length, 27);
  assert.equal(calls[0].url, 'http://example.test/api/v1/tenancy/context');
  assert.deepEqual(JSON.parse(calls[0].options.body), { tenantId: null });
  const transitions = new Map([[0, initialAccessToken], [1, tokens[0]], [2, tokens[0]], [3, tokens[1]], [4, tokens[1]], [5, tokens[2]], [11, tokens[2]], [12, tokens[3]], [19, tokens[3]], [20, tokens[4]], [24, tokens[4]], [25, tokens[5]]]);
  let currentToken;
  for (let index = 0; index < calls.length; index++) {
    if (transitions.has(index)) currentToken = transitions.get(index);
    assert.equal(calls[index].options.headers.Authorization, `Bearer ${currentToken}`, 'reused stale token at request ' + index);
    assert.equal(calls[index].options.redirect, 'error');
    assert.ok(calls[index].options.signal instanceof AbortSignal);
    if (calls[index].url.includes('/catalog/products') && calls[index].options.body) assert.equal(Object.hasOwn(JSON.parse(calls[index].options.body), 'tenantId'), false);
  }
  const evidence = readFileSync(logPath, 'utf8');
  assert.equal(JSON.parse(evidence).completed, true);
  assert.equal(JSON.parse(evidence).responses.length, 27);
  for (const token of [initialAccessToken, ...tokens]) assert.equal(evidence.includes(token), false);
}));
const failures = { 'wrong-context': 5, 'cross-read-leak': 6, 'cross-list-leak': 7, 'cross-update-allowed': 8, 'cross-delete-allowed': 9,
  'local-row-mutated': 13, 'reverse-read-leak': 14, 'reverse-list-leak': 15, 'reverse-update-allowed': 16, 'reverse-delete-allowed': 17, 'other-row-mutated': 21, 'request-error': 6 };
for (const [failure, expectedCalls] of Object.entries(failures)) {
  test(`tenant isolation acceptance rejects ${failure} at the intended step`, async () => fixture(async (logPath) => {
    const calls = [];
    await assert.rejects(() => verifyApplicationCrudTenantIsolation('http://example.test', { localTenantId, initialAccessToken, logPath, request: runner(calls, failure) }), (error) => ![initialAccessToken, ...tokens].some((token) => error.message.includes(token)));
    assert.equal(calls.length, expectedCalls);
    const evidence = readFileSync(logPath, 'utf8');
    assert.equal(JSON.parse(evidence).completed, false);
    assert.equal(JSON.parse(evidence).responses.length, calls.length);
    for (const token of [initialAccessToken, ...tokens]) assert.equal(evidence.includes(token), false);
  }));
}
