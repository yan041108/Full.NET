import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { verifyApplicationCrudTenantHttp } from './support/application-crud-tenant-http.mjs';

const hostAccessToken = 'secret-host-token';
const tenantAccessToken = 'secret-tenant-context-token';
const tenantId = '01900000-0000-7000-8000-000000000010';
const id = '01900000-0000-7000-8000-000000000011';
const original = { id, tenantId, name: 'Application tenant CRUD probe', version: '1' };
const updated = { ...original, name: 'Updated application tenant CRUD probe', version: '2' };
const context = { tenantId, identifier: 'local', scope: `tenant:${tenantId.replaceAll('-', '')}` };
const successResult = { businessRequests: 11, versionConflicts: 2, deleted: true, tenantId, tenantAccessToken };
function response(body, status = 200, problem = false) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': problem ? 'application/problem+json' : 'application/json' } });
}
function runner(calls, failure) {
  const responses = [
    response([{ id: tenantId, identifier: 'local' }]), response({ accessToken: tenantAccessToken, context }),
    response(original, 201), response(original), response({ items: [original] }), response(updated),
    response({ status: 409, code: 'catalog.products.version_conflict' }, 409, true), response(updated),
    response({ status: 409, code: 'catalog.products.version_conflict' }, 409, true), response(updated), response(updated),
    response({ status: 404, code: 'catalog.products.not_found' }, 404, true), response({ items: [] }),
  ];
  return async (url, options) => {
    const index = calls.length;
    calls.push({ url, options });
    if (failure === 'no-local' && index === 0) return response([{ id: tenantId, identifier: 'other' }]);
    if (failure === 'wrong-context' && index === 1) return response({ accessToken: tenantAccessToken, context: { ...context, tenantId: id } });
    if (failure === 'missing-token' && index === 1) return response({ context });
    if (failure === 'credential-json-error' && index === 1) return new Response(`invalid ${tenantAccessToken}`, { status: 200 });
    if (failure === 'wrong-tenant' && index === 2) return response({ ...original, tenantId: id }, 201);
    if (failure === 'reflected-token-name' && index === 2) return response({ ...original, name: tenantAccessToken }, 201);
    if (failure === 'wrong-version' && index === 5) return response({ ...updated, version: '1' });
    if (failure === 'stale-update-accepted' && index === 6) return response(original);
    if (failure === 'conflict-mutated' && index === 7) return response({ ...updated, name: 'lost update' });
    if (failure === 'stale-delete-accepted' && index === 8) return response(updated);
    if (failure === 'deleted-row-retained' && index === 12) return response({ items: [updated] });
    if (failure === 'request-error' && index === 3) throw new Error(`transport failed ${tenantAccessToken}`);
    assert.ok(index < responses.length, 'unexpected request');
    return responses[index];
  };
}
async function fixture(action) {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-tenant-crud-http-'));
  try { await action(join(root, 'result.json')); } finally { rmSync(root, { recursive: true, force: true }); }
}
test('uses trusted context API and executes versioned tenant CRUD with conflict preservation', async () => fixture(async (logPath) => {
  const calls = [];
  assert.deepEqual(await verifyApplicationCrudTenantHttp('http://example.test', { hostAccessToken, logPath, request: runner(calls) }), successResult);
  assert.equal(calls.length, 13);
  assert.equal(calls[0].url, 'http://example.test/api/v1/tenancy/available');
  assert.equal(calls[1].options.method, 'PUT');
  assert.deepEqual(JSON.parse(calls[1].options.body), { tenantId });
  for (let index = 0; index < calls.length; index++) {
    const options = calls[index].options;
    assert.equal(options.headers.Authorization, 'Bearer ' + (index < 2 ? hostAccessToken : tenantAccessToken));
    assert.equal(options.redirect, 'error');
    assert.ok(options.signal instanceof AbortSignal);
    if (index > 1 && options.body) assert.equal(Object.hasOwn(JSON.parse(options.body), 'tenantId'), false);
  }
  assert.deepEqual(JSON.parse(calls[6].options.body), { name: 'Stale update must be rejected', version: '1' });
  assert.deepEqual(JSON.parse(calls[8].options.body), { version: '1' });
  assert.deepEqual(JSON.parse(calls[10].options.body), { version: '2' });
  const evidence = readFileSync(logPath, 'utf8');
  assert.equal(JSON.parse(evidence).completed, true);
  assert.equal(JSON.parse(evidence).responses.length, 13);
  assert.equal(evidence.includes(hostAccessToken), false);
  assert.equal(evidence.includes(tenantAccessToken), false);
}));
for (const failure of ['no-local', 'wrong-context', 'missing-token', 'credential-json-error', 'wrong-tenant', 'reflected-token-name', 'wrong-version', 'stale-update-accepted', 'conflict-mutated', 'stale-delete-accepted', 'deleted-row-retained', 'request-error']) {
  test(`tenant CRUD acceptance rejects ${failure} and preserves sanitized partial evidence`, async () => fixture(async (logPath) => {
    const calls = [];
    await assert.rejects(() => verifyApplicationCrudTenantHttp('http://example.test', { hostAccessToken, logPath, request: runner(calls, failure) }), (error) => !error.message.includes(hostAccessToken) && !error.message.includes(tenantAccessToken));
    const expectedCalls = { 'no-local': 1, 'wrong-context': 2, 'missing-token': 2, 'credential-json-error': 2, 'wrong-tenant': 3, 'reflected-token-name': 3, 'wrong-version': 6,
      'stale-update-accepted': 7, 'conflict-mutated': 8, 'stale-delete-accepted': 9, 'deleted-row-retained': 13, 'request-error': 4 };
    assert.equal(calls.length, expectedCalls[failure], 'failure did not reach the intended stage');
    const evidence = readFileSync(logPath, 'utf8');
    assert.equal(JSON.parse(evidence).completed, false);
    assert.equal(JSON.parse(evidence).responses.length, calls.length);
    assert.equal(evidence.includes(hostAccessToken), false);
    assert.equal(evidence.includes(tenantAccessToken), false);
  }));
}

test('tenant CRUD invokes a generated-client read after validating the created product', async () => fixture(async (logPath) => {
  const calls = [];
  const observed = [];
  const result = await verifyApplicationCrudTenantHttp('http://example.test', {
    hostAccessToken, logPath, request: runner(calls),
    onCreatedProduct: async (value) => observed.push({ ...value, atRequest: calls.length }),
  });
  assert.deepEqual(result, successResult);
  assert.deepEqual(observed, [{ tenantAccessToken, product: original, atRequest: 3 }]);
  assert.equal(readFileSync(logPath, 'utf8').includes(tenantAccessToken), false);
}));

test('tenant CRUD stops before later mutations when the generated-client read fails', async () => fixture(async (logPath) => {
  const calls = [];
  await assert.rejects(() => verifyApplicationCrudTenantHttp('http://example.test', {
    hostAccessToken, logPath, request: runner(calls),
    onCreatedProduct: async () => { throw new Error('generated read failed'); },
  }), /generated read failed/u);
  assert.equal(calls.length, 3);
  assert.equal(JSON.parse(readFileSync(logPath, 'utf8')).completed, false);
}));
