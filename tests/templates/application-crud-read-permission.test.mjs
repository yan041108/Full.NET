import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { verifyApplicationCrudReadPermission, verifyApplicationCrudNoPermission, verifyApplicationCrudCreatePermission, verifyApplicationCrudUpdatePermission, verifyApplicationCrudDeletePermission } from './support/application-crud-read-permission.mjs';

const tenantId = '01900000-0000-7000-8000-000000000010';
const id = '01900000-0000-7000-8000-000000000011';
const roleId = '01900000-0000-7000-8000-000000000012';
const userId = '01900000-0000-7000-8000-000000000013';
const permissions = ['catalog.products.read', 'identity.navigation.read', 'platform.dashboard.read', 'tenancy.tenants.read', 'tenancy.tenants.switch'];
const product = { id, tenantId, name: 'Read permission product', version: '1' };
const createdProduct = { id: '01900000-0000-7000-8000-000000000014', tenantId, name: 'Ordinary account created product', version: '1' };
const updatedProduct = { ...product, name: 'Ordinary account updated product', version: '2' };
const tokens = ['secret-admin', 'secret-login', 'secret-password', 'secret-admin-tenant', 'secret-reader-tenant', 'secret-admin-host', 'secret-csrf'];
const context = { tenantId, identifier: 'local', scope: `tenant:${tenantId.replaceAll('-', '')}` };
const problem = { status: 403, code: 'authorization.permission_denied' };
const bodies = [
  [{ id: tenantId, identifier: 'local' }],
  { id: roleId, version: 1, isSystem: false, isSuperAdministrator: false },
  { id: roleId, version: 2, permissionCodes: permissions, isSuperAdministrator: false },
  { id: userId, version: 1 }, { userId, roleIds: [], version: 1 }, { userId, roleIds: [roleId], version: 2 },
  { accessToken: tokens[1] }, { accessToken: tokens[2] },
  { id: userId, scope: 'host', tenantId: null, isSuperAdministrator: false, passwordChangeRequired: false, permissions: ['identity.navigation.read', 'platform.dashboard.read', 'tenancy.tenants.read', 'tenancy.tenants.switch'] },
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
function runner(calls, change, noProductPermission = false, canCreate = false, canUpdate = false, canDelete = false) {
  return async (url, options) => {
    const i = calls.length;
    calls.push({ url, options });
    // 替身遵守真实角色API的父页面闭包，不能固定200掩盖缺失tenancy.read。
    const expectedPermissions = noProductPermission ? permissions.slice(1) : canCreate ? ['catalog.products.create', ...permissions] : canUpdate ? [permissions[0], 'catalog.products.update', ...permissions.slice(1)] : canDelete ? ['catalog.products.disable', ...permissions] : permissions;
    if (i === 2) assert.deepEqual(JSON.parse(options.body).permissionCodes, expectedPermissions);
    const altered = change?.(i);
    const denied = i >= (noProductPermission ? 13 : canCreate ? 16 : 15) && i <= 17;
    let defaultBody = bodies[i];
    if (noProductPermission && [2, 12].includes(i)) defaultBody = { ...defaultBody, [i === 2 ? 'permissionCodes' : 'permissions']: expectedPermissions };
    if (noProductPermission && [13, 14].includes(i)) defaultBody = problem;
    if (canCreate) {
      if ([2, 12].includes(i)) defaultBody = { ...defaultBody, [i === 2 ? 'permissionCodes' : 'permissions']: expectedPermissions };
      if ([15, 21].includes(i)) defaultBody = createdProduct;
      if (i === 19) defaultBody = { items: [product, createdProduct] };
      if (i === 22) defaultBody = { status: 404, code: 'catalog.products.not_found' };
      if (i === 23) defaultBody = bodies[21];
    }
    if (canUpdate) {
      if ([2, 12].includes(i)) defaultBody = { ...defaultBody, [i === 2 ? 'permissionCodes' : 'permissions']: expectedPermissions };
      if ([16, 18, 20, 22].includes(i)) defaultBody = updatedProduct;
      if (i === 17) defaultBody = { status: 409, code: 'catalog.products.version_conflict' };
      if (i === 19) defaultBody = problem;
      if (i === 21) defaultBody = { items: [updatedProduct] };
      if (i === 23) defaultBody = bodies[21];
    }
    if (canDelete) {
      if ([2, 12].includes(i)) defaultBody = { ...defaultBody, [i === 2 ? 'permissionCodes' : 'permissions']: expectedPermissions };
      if (i === 17) defaultBody = { status: 409, code: 'catalog.products.version_conflict' };
      if ([18, 19].includes(i)) defaultBody = product;
      if ([20, 22].includes(i)) defaultBody = { status: 404, code: 'catalog.products.not_found' };
      if ([21, 23].includes(i)) defaultBody = { items: [] };
      if (i === 24) defaultBody = bodies[21];
    }
    const data = altered?.body ?? defaultBody;
    const status = canDelete && i >= 17 ? i === 17 ? 409 : [20, 22].includes(i) ? 404 : 200 : canUpdate && i >= 16 ? i === 17 ? 409 : i === 19 ? 403 : 200 : denied ? 403 : canCreate && i === 15 ? 201 : canCreate && i === 22 ? 404 : canCreate && i >= 21 ? 200 : statusAt(i);
    return new Response(altered?.raw ?? JSON.stringify(data), { status: altered?.status ?? status, headers: {
      'content-type': altered?.contentType ?? ([403, 404, 409].includes(status) ? 'application/problem+json' : 'application/json'),
      ...(i === 6 ? { 'set-cookie': `fullnet-csrf=${tokens[6]}; Path=/; SameSite=Lax` } : {}),
    } });
  };
}

test('ordinary delete account rejects mismatched version then deletes without create or update permission', async () => fixture(async (logPath) => {
  const calls = [];
  const result = await verifyApplicationCrudDeletePermission('http://example.test', { hostAccessToken: tokens[0], logPath, request: runner(calls, undefined, false, false, false, true) });
  assert.deepEqual(result, { businessRequests: 12, readAllowed: 2, deleteAllowed: 1, versionConflicts: 1, writeDenied: 2, rowPreserved: true, deleted: true, hostAccessToken: tokens[5] });
  assert.equal(calls.length, 25);
  assert.equal(JSON.parse(calls[1].options.body).code, 'catalog-delete-probe');
  assert.equal(JSON.parse(calls[3].options.body).username, 'catalog-deleter-probe');
  assert.deepEqual(JSON.parse(calls[2].options.body).permissionCodes, ['catalog.products.disable', ...permissions]);
  assert.deepEqual(JSON.parse(calls[17].options.body), { version: '2' });
  assert.deepEqual(JSON.parse(calls[19].options.body), { version: '1' });
  for (const i of [17, 18, 19, 20, 21]) assert.equal(calls[i].options.headers.Authorization, `Bearer ${tokens[4]}`);
  for (const i of [22, 23]) assert.equal(calls[i].options.headers.Authorization, `Bearer ${tokens[3]}`);
  const text = readFileSync(logPath, 'utf8');
  const evidence = JSON.parse(text);
  assert.equal(evidence.completed, true);
  assert.equal(evidence.responses.filter((entry) => entry.status === 404).length, 2);
  assert.equal(Object.hasOwn(evidence.result, 'hostAccessToken'), false);
  for (const token of tokens) assert.equal(text.includes(token), false);
}));

for (const [name, index, altered] of [
  ['missing-disable-permission', 2, { body: bodies[2] }],
  ['account-has-update', 12, { body: { ...bodies[12], permissions: ['catalog.products.disable', ...permissions, 'catalog.products.update'] } }],
  ['create-allowed', 15, { status: 201, body: createdProduct }],
  ['update-allowed', 16, { status: 200, body: updatedProduct }],
  ['mismatched-delete-allowed', 17, { status: 200, body: product }],
  ['conflict-changed-row', 18, { body: { ...product, version: '2' } }],
  ['delete-denied', 19, { status: 403, body: problem }],
  ['delete-returned-wrong-row', 19, { body: createdProduct }],
  ['ordinary-read-leaks-deleted', 20, { status: 200, body: product }],
  ['ordinary-list-retains-row', 21, { body: { items: [product] } }],
  ['admin-read-still-present', 22, { status: 200, body: product }],
  ['admin-list-still-present', 23, { body: { items: [product] } }],
]) {
  test(`ordinary delete permission rejects ${name} at the intended stage`, async () => fixture(async (logPath) => {
    const calls = [];
    await assert.rejects(() => verifyApplicationCrudDeletePermission('http://example.test', {
      hostAccessToken: tokens[0], logPath, request: runner(calls, (i) => i === index ? altered : undefined, false, false, false, true),
    }), (error) => tokens.every((token) => !error.message.includes(token)));
    assert.equal(calls.length, index + 1, 'failure did not reach intended stage');
    const text = readFileSync(logPath, 'utf8');
    assert.equal(JSON.parse(text).completed, false);
    assert.equal(JSON.parse(text).responses.length, calls.length);
    for (const token of tokens) assert.equal(text.includes(token), false);
  }));
}

test('ordinary update account changes version, rejects stale update and cannot create or delete', async () => fixture(async (logPath) => {
  const calls = [];
  const result = await verifyApplicationCrudUpdatePermission('http://example.test', { hostAccessToken: tokens[0], logPath, request: runner(calls, undefined, false, false, true) });
  assert.deepEqual(result, { businessRequests: 11, readAllowed: 2, updateAllowed: 1, versionConflicts: 1, writeDenied: 2, rowPreserved: true, hostAccessToken: tokens[5] });
  assert.equal(calls.length, 24);
  assert.equal(JSON.parse(calls[1].options.body).code, 'catalog-update-probe');
  assert.equal(JSON.parse(calls[3].options.body).username, 'catalog-updater-probe');
  assert.deepEqual(JSON.parse(calls[2].options.body).permissionCodes, [permissions[0], 'catalog.products.update', ...permissions.slice(1)]);
  assert.deepEqual(JSON.parse(calls[16].options.body), { name: updatedProduct.name, version: '1' });
  assert.equal(JSON.parse(calls[17].options.body).version, '1');
  assert.equal(JSON.parse(calls[19].options.body).version, '2');
  assert.equal(JSON.parse(calls[22].options.body).version, '2');
  for (const i of [15, 16, 17, 18, 19]) assert.equal(calls[i].options.headers.Authorization, `Bearer ${tokens[4]}`);
  const text = readFileSync(logPath, 'utf8');
  assert.equal(JSON.parse(text).completed, true);
  assert.equal(Object.hasOwn(JSON.parse(text).result, 'hostAccessToken'), false);
  for (const token of tokens) assert.equal(text.includes(token), false);
}));

for (const [name, index, altered] of [
  ['missing-update-permission', 2, { body: bodies[2] }],
  ['update-wrong-version', 16, { body: { ...updatedProduct, version: '1' } }],
  ['update-wrong-tenant', 16, { body: { ...updatedProduct, tenantId: roleId } }],
  ['stale-update-allowed', 17, { status: 200, body: updatedProduct }],
  ['wrong-conflict-code', 17, { body: { status: 409, code: 'other.conflict' } }],
  ['conflict-changed-row', 18, { body: { ...updatedProduct, name: 'Unexpected stale overwrite' } }],
  ['delete-allowed', 19, { status: 200, body: updatedProduct }],
  ['update-not-persisted', 20, { body: product }],
  ['list-retains-old-version', 21, { body: { items: [product] } }],
  ['cleanup-returns-old-version', 22, { body: product }],
]) {
  test(`ordinary update permission rejects ${name} at the intended stage`, async () => fixture(async (logPath) => {
    const calls = [];
    await assert.rejects(() => verifyApplicationCrudUpdatePermission('http://example.test', {
      hostAccessToken: tokens[0], logPath, request: runner(calls, (i) => i === index ? altered : undefined, false, false, true),
    }), (error) => tokens.every((token) => !error.message.includes(token)));
    assert.equal(calls.length, index + 1, 'failure did not reach intended stage');
    const text = readFileSync(logPath, 'utf8');
    assert.equal(JSON.parse(text).completed, false);
    assert.equal(JSON.parse(text).responses.length, calls.length);
    for (const token of tokens) assert.equal(text.includes(token), false);
  }));
}

test('ordinary create account creates its own tenant row but cannot update or delete', async () => fixture(async (logPath) => {
  const calls = [];
  const result = await verifyApplicationCrudCreatePermission('http://example.test', { hostAccessToken: tokens[0], logPath, request: runner(calls, undefined, false, true) });
  assert.deepEqual(result, { businessRequests: 11, readAllowed: 2, createAllowed: 1, writeDenied: 2, rowPreserved: true, createdRowDeleted: true, hostAccessToken: tokens[5] });
  assert.equal(calls.length, 24);
  assert.equal(JSON.parse(calls[1].options.body).code, 'catalog-create-probe');
  assert.equal(JSON.parse(calls[3].options.body).username, 'catalog-creator-probe');
  assert.deepEqual(JSON.parse(calls[2].options.body).permissionCodes, ['catalog.products.create', ...permissions]);
  assert.equal(calls[15].options.headers.Authorization, `Bearer ${tokens[4]}`);
  assert.deepEqual(JSON.parse(calls[15].options.body), { name: createdProduct.name });
  assert.equal(calls[21].url, 'http://example.test/api/v1/catalog/products/' + createdProduct.id + '/delete');
  assert.equal(calls[22].url, 'http://example.test/api/v1/catalog/products/' + createdProduct.id);
  const text = readFileSync(logPath, 'utf8');
  const evidence = JSON.parse(text);
  assert.equal(evidence.completed, true);
  assert.equal(evidence.responses.filter((entry) => entry.status === 403).length, 2);
  assert.equal(Object.hasOwn(evidence.result, 'hostAccessToken'), false);
  for (const token of tokens) assert.equal(text.includes(token), false);
}));

const createFailures = [
  ['role-missing-create', 2, { body: bodies[2] }],
  ['account-has-update', 12, { body: { ...bodies[12], permissions: ['catalog.products.create', ...permissions, 'catalog.products.update'] } }],
  ['create-denied', 15, { status: 403, body: problem }],
  ['created-wrong-tenant', 15, { body: { ...createdProduct, tenantId: roleId } }],
  ['created-wrong-version', 15, { body: { ...createdProduct, version: 1 } }],
  ['created-invalid-id', 15, { body: { ...createdProduct, id: 'invalid' } }],
  ['created-overwrites-existing', 15, { body: { ...createdProduct, id } }],
  ['update-allowed', 16, { status: 200, body: product }],
  ['delete-allowed', 17, { status: 200, body: product }],
  ['original-mutated', 18, { body: { ...product, version: '2' } }],
  ['created-not-persisted', 19, { body: { items: [product] } }],
  ['created-mutated', 19, { body: { items: [product, { ...createdProduct, name: 'Unexpected mutation' }] } }],
  ['created-cleanup-wrong-row', 21, { body: product }],
  ['deleted-row-still-readable', 22, { status: 200, body: createdProduct }],
  ['deleted-wrong-code', 22, { body: { status: 404, code: 'other.not_found' } }],
  ['wrong-host-return', 23, { body: { accessToken: tokens[5], context } }],
];
for (const [name, index, altered] of createFailures) {
  test(`ordinary create permission rejects ${name} at the intended stage`, async () => fixture(async (logPath) => {
    const calls = [];
    await assert.rejects(() => verifyApplicationCrudCreatePermission('http://example.test', {
      hostAccessToken: tokens[0], logPath, request: runner(calls, (i) => i === index ? altered : undefined, false, true),
    }), (error) => tokens.every((token) => !error.message.includes(token)));
    assert.equal(calls.length, index + 1, 'failure did not reach intended stage');
    const text = readFileSync(logPath, 'utf8');
    assert.equal(JSON.parse(text).completed, false);
    assert.equal(JSON.parse(text).responses.length, calls.length);
    for (const token of tokens) assert.equal(text.includes(token), false);
  }));
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

test('ordinary browser check receives the active account before business requests without logging credentials', async () => fixture(async (logPath) => {
  const calls = [];
  const observed = [];
  const browserTenantToken = 'secret-browser-tenant';
  await verifyApplicationCrudReadPermission('http://example.test', {
    hostAccessToken: tokens[0], logPath, request: runner(calls),
    onTenantAccount: async (account) => {
      observed.push(account);
      assert.equal(calls.length, 13);
      assert.equal(account.mode, 'read');
      assert.equal(account.username, 'catalog-reader-probe');
      assert.match(account.password, /^Bb2!/u);
      assert.equal(account.tenantId, tenantId);
      return browserTenantToken;
    },
  });
  assert.equal(observed.length, 1);
  assert.equal(calls[13].options.headers.Authorization, `Bearer ${browserTenantToken}`);
  const report = readFileSync(logPath, 'utf8');
  assert.equal(report.includes(observed[0].password), false);
  assert.equal(report.includes(observed[0].username), false);
  assert.equal(report.includes(browserTenantToken), false);
}));

for (const [mode, verify] of [
  ['create', verifyApplicationCrudCreatePermission],
  ['update', verifyApplicationCrudUpdatePermission],
  ['delete', verifyApplicationCrudDeletePermission],
]) {
  test(`browser ${mode} action is checked through administrator persistence without changing the permission probe`, async () => fixture(async (logPath) => {
    const calls = [];
    const browserCalls = [];
    const browserId = '01900000-0000-7000-8000-000000000015';
    const targetName = `Browser ${mode} target`;
    const browserRow = { id: browserId, tenantId, name: mode === 'create' ? 'Browser created product' : targetName, version: '1' };
    const baseRequest = runner(calls, undefined, false, mode === 'create', mode === 'update', mode === 'delete');
    const reply = (body, status = 200) => new Response(JSON.stringify(body), { status,
      headers: { 'content-type': status === 404 ? 'application/problem+json' : 'application/json' } });
    const request = (url, options) => {
      const path = new URL(url).pathname;
      const body = options.body ? JSON.parse(options.body) : undefined;
      if (mode !== 'create' && path.endsWith('/catalog/products/') && body?.name === targetName) {
        browserCalls.push('seed');
        return reply(browserRow, 201);
      }
      if (mode === 'create' && path.endsWith('/catalog/products/') && url.includes('pageSize=20')) {
        browserCalls.push('list-created');
        return reply({ items: [product, browserRow] });
      }
      if (path.includes(browserId)) {
        browserCalls.push(options.method === 'GET' ? 'read' : 'cleanup');
        if (mode === 'delete') return reply({ status: 404, code: 'catalog.products.not_found' }, 404);
        if (mode === 'update' && options.method === 'GET') return reply({ ...browserRow, name: 'Browser updated product', version: '2' });
        return reply(browserRow);
      }
      return baseRequest(url, options);
    };
    const result = await verify('http://example.test', { hostAccessToken: tokens[0], logPath, request,
      onTenantAccount: async (account) => {
        assert.equal(account.mode, mode);
        assert.equal(account.actionTargetName, mode === 'create' ? undefined : targetName);
        return 'secret-browser-tenant';
      } });
    assert.equal(result.hostAccessToken, tokens[5]);
    assert.deepEqual(browserCalls, mode === 'create' ? ['list-created', 'cleanup']
      : mode === 'update' ? ['seed', 'read', 'cleanup'] : ['seed', 'read']);
    assert.equal(JSON.parse(readFileSync(logPath, 'utf8')).completed, true);
    assert.equal(readFileSync(logPath, 'utf8').includes('secret-browser-tenant'), false);
  }));
}
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
