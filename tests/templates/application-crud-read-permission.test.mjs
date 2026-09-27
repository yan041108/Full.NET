import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { verifyApplicationCrudReadPermission, verifyApplicationCrudNoPermission } from './support/application-crud-read-permission.mjs';

const tenantId = '01900000-0000-7000-8000-000000000010';
const id = '01900000-0000-7000-8000-000000000011';
const roleId = '01900000-0000-7000-8000-000000000012';
const userId = '01900000-0000-7000-8000-000000000013';
const permissions = ['catalog.products.read', 'tenancy.tenants.read', 'tenancy.tenants.switch'];
const product = { id, tenantId, name: 'Read permission product', version: '1' };
const tokens = ['secret-admin', 'secret-login', 'secret-password', 'secret-admin-tenant', 'secret-reader-tenant', 'secret-admin-host', 'secret-csrf'];
const context = { tenantId, identifier: 'local', scope: `tenant:${tenantId.replaceAll('-', '')}` };
const problem = { status: 403, code: 'authorization.permission_denied' };
const bodies = [
  [{ id: tenantId, identifier: 'local' }],
  { id: roleId, version: 1, isSystem: false, isSuperAdministrator: false },
  { id: roleId, version: 2, permissionCodes: permissions, isSuperAdministrator: false },
  { id: userId, version: 1 }, { userId, roleIds: [], version: 1 }, { userId, roleIds: [roleId], version: 2 },
  { accessToken: tokens[1] }, { accessToken: tokens[2] },
  { id: userId, scope: 'host', tenantId: null, isSuperAdministrator: false, passwordChangeRequired: false, permissions: ['tenancy.tenants.read', 'tenancy.tenants.switch'] },
  { accessToken: tokens[3], context }, product,
  { accessToken: tokens[4], context },
  { id: userId, tenantId, scope: context.scope, isSuperAdministrator: false, passwordChangeRequired: false, permissions },
  { items: [product] }, product, problem, problem, problem, product, { items: [product] }, product,
  { accessToken: tokens[5], context: { tenantId: null, scope: 'host' } },
];
const statusAt = (i) => [1, 3, 10].includes(i) ? 201 : [15, 16, 17].includes(i) ? 403 : 200;
async function fixture(action) {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-read-permission-'));
  try { await action(join(root, 'result.json')); } finally { rmSync(root, { recursive: true, force: true }); }
}
function runner(calls, change, noProductPermission = false) {
  return async (url, options) => {
    const i = calls.length;
    calls.push({ url, options });
    // 替身遵守真实角色API的父页面闭包，不能固定200掩盖缺失tenancy.read。
    const expectedPermissions = noProductPermission ? permissions.slice(1) : permissions;
    if (i === 2) assert.deepEqual(JSON.parse(options.body).permissionCodes, expectedPermissions);
    const altered = change?.(i);
    const denied = i >= (noProductPermission ? 13 : 15) && i <= 17;
    let defaultBody = bodies[i];
    if (noProductPermission && [2, 12].includes(i)) defaultBody = { ...defaultBody, [i === 2 ? 'permissionCodes' : 'permissions']: expectedPermissions };
    if (noProductPermission && [13, 14].includes(i)) defaultBody = problem;
    const data = altered?.body ?? defaultBody;
    return new Response(altered?.raw ?? JSON.stringify(data), { status: altered?.status ?? (denied ? 403 : statusAt(i)), headers: {
      'content-type': altered?.contentType ?? (denied ? 'application/problem+json' : 'application/json'),
      ...(i === 6 ? { 'set-cookie': `fullnet-csrf=${tokens[6]}; Path=/; SameSite=Lax` } : {}),
    } });
  };
}

test('authenticated tenant account without product permissions denies all five business routes', async () => fixture(async (logPath) => {
  const calls = [];
  const result = await verifyApplicationCrudNoPermission('http://example.test', { hostAccessToken: tokens[0], logPath, request: runner(calls, undefined, true) });
  assert.deepEqual(result, { businessRequests: 9, readAllowed: 0, readDenied: 2, writeDenied: 3, rowPreserved: true, hostAccessToken: tokens[5] });
  assert.equal(calls.length, 22);
  assert.equal(JSON.parse(calls[1].options.body).code, 'catalog-no-permission-probe');
  assert.equal(JSON.parse(calls[3].options.body).username, 'catalog-unprivileged-probe');
  assert.equal(JSON.parse(calls[6].options.body).username, 'catalog-unprivileged-probe');
  assert.deepEqual(JSON.parse(calls[2].options.body).permissionCodes, permissions.slice(1));
  assert.deepEqual(calls.slice(13, 18).map(({ url, options }) => [url.replace('http://example.test', ''), options.method]), [
    ['/api/v1/catalog/products/?page=1&pageSize=5', 'GET'], ['/api/v1/catalog/products/' + id, 'GET'],
    ['/api/v1/catalog/products/', 'POST'], ['/api/v1/catalog/products/' + id, 'PUT'], ['/api/v1/catalog/products/' + id + '/delete', 'POST'],
  ]);
  for (const i of [13, 14, 15, 16, 17]) assert.equal(calls[i].options.headers.Authorization, `Bearer ${tokens[4]}`);
  const text = readFileSync(logPath, 'utf8');
  const evidence = JSON.parse(text);
  assert.equal(evidence.completed, true);
  assert.equal(evidence.responses.filter((entry) => entry.status === 403).length, 5);
  assert.equal(Object.hasOwn(evidence.result, 'hostAccessToken'), false);
  for (const token of tokens) assert.equal(text.includes(token), false);
}));

const noPermissionFailures = [
  ['role-grants-product-read', 2, { body: bodies[2] }],
  ['account-has-product-read', 12, { body: bodies[12] }],
  ['list-allowed', 13, { status: 200, body: { items: [product] } }],
  ['read-allowed', 14, { status: 200, body: product }],
  ['create-allowed', 15, { status: 201, body: product }],
  ['update-allowed', 16, { status: 200, body: product }],
  ['delete-allowed', 17, { status: 200, body: product }],
  ['wrong-denial-code', 13, { body: { ...problem, code: 'other.denied' } }],
  ['wrong-denial-type', 14, { contentType: 'application/json' }],
  ['credential-json', 7, { raw: `invalid ${tokens[2]}` }],
  ['row-mutated', 18, { body: { ...product, version: '2' } }],
  ['denied-create-persisted', 19, { body: { items: [product, { ...product, id: roleId }] } }],
];
for (const [name, index, altered] of noPermissionFailures) {
  test(`no product permission acceptance rejects ${name} at the intended stage`, async () => fixture(async (logPath) => {
    const calls = [];
    await assert.rejects(() => verifyApplicationCrudNoPermission('http://example.test', {
      hostAccessToken: tokens[0], logPath, request: runner(calls, (i) => i === index ? altered : undefined, true),
    }), (error) => tokens.every((token) => !error.message.includes(token)));
    assert.equal(calls.length, index + 1, 'failure did not reach intended stage');
    const text = readFileSync(logPath, 'utf8');
    assert.equal(JSON.parse(text).completed, false);
    assert.equal(JSON.parse(text).responses.length, calls.length);
    for (const token of tokens) assert.equal(text.includes(token), false);
  }));
}
test('ordinary read-only account uses real API setup, password change and exact tenant permissions', async () => fixture(async (logPath) => {
  const calls = [];
  const result = await verifyApplicationCrudReadPermission('http://example.test', { hostAccessToken: tokens[0], logPath, request: runner(calls) });
  assert.deepEqual(result, { businessRequests: 9, readAllowed: 2, writeDenied: 3, rowPreserved: true, hostAccessToken: tokens[5] });
  assert.equal(calls.length, 22);
  assert.equal(calls[7].options.headers['X-CSRF-Token'], tokens[6]);
  assert.equal(calls[7].options.headers.Cookie, `fullnet-csrf=${tokens[6]}`);
  assert.deepEqual(JSON.parse(calls[2].options.body).permissionCodes, permissions);
  assert.deepEqual(JSON.parse(calls[5].options.body).roleIds, [roleId]);
  for (const i of [13, 14, 15, 16, 17]) assert.equal(calls[i].options.headers.Authorization, `Bearer ${tokens[4]}`);
  for (const call of calls) { assert.equal(call.options.redirect, 'error'); assert.ok(call.options.signal); }
  const text = readFileSync(logPath, 'utf8');
  const evidence = JSON.parse(text);
  assert.equal(evidence.completed, true);
  assert.equal(evidence.responses.length, 22);
  assert.equal(Object.hasOwn(evidence.result, 'hostAccessToken'), false);
  for (const token of tokens) assert.equal(text.includes(token), false);
}));
const failures = [
  ['super-role', 1, { body: { ...bodies[1], isSuperAdministrator: true } }],
  ['extra-role-permission', 2, { body: { ...bodies[2], permissionCodes: [...permissions, 'catalog.products.create'] } }],
  ['wrong-user-role', 5, { body: { ...bodies[5], roleIds: [] } }],
  ['missing-csrf', 6, { body: bodies[6] }],
  ['credential-json', 7, { raw: `invalid ${tokens[2]}` }],
  ['password-still-required', 8, { body: { ...bodies[8], passwordChangeRequired: true } }],
  ['tenant-super-user', 12, { body: { ...bodies[12], isSuperAdministrator: true } }],
  ['wrong-tenant', 12, { body: { ...bodies[12], tenantId: id } }],
  ['extra-user-permission', 12, { body: { ...bodies[12], permissions: [...permissions, 'catalog.products.update'] } }],
  ['missing-list-row', 13, { body: { items: [] } }],
  ['create-allowed', 15, { status: 201, body: product }],
  ['wrong-problem-code', 16, { body: { ...problem, code: 'other.denied' } }],
  ['wrong-problem-type', 17, { contentType: 'application/json' }],
  ['row-mutated', 18, { body: { ...product, name: 'Unexpected mutation' } }],
  ['denied-create-persisted', 19, { body: { items: [product, { ...product, id: roleId, name: 'Forbidden create' }] } }],
  ['wrong-return-context', 21, { body: { accessToken: tokens[5], context } }],
];
for (const [name, index, altered] of failures) {
  test(`ordinary read permission rejects ${name} at the intended stage`, async () => fixture(async (logPath) => {
    const calls = [];
    const fake = runner(calls, (i) => i === index ? altered : undefined);
    const request = name === 'missing-csrf' ? async (...args) => { const response = await fake(...args); if (calls.length === 7) response.headers.delete('set-cookie'); return response; } : fake;
    await assert.rejects(() => verifyApplicationCrudReadPermission('http://example.test', { hostAccessToken: tokens[0], logPath, request }));
    assert.equal(calls.length, index + 1, 'failure did not reach intended stage');
    const text = readFileSync(logPath, 'utf8');
    assert.equal(JSON.parse(text).completed, false);
    assert.equal(JSON.parse(text).responses.length, calls.length);
    for (const token of tokens) assert.equal(text.includes(token), false);
  }));
}
