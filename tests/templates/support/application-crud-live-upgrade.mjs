import assert from 'node:assert/strict';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

const base = '/api/v1/catalog/products';

export async function renewTenantSession(apiUrl, adminPassword, expectedTenantId) {
  const login = await fetch(`${apiUrl}/api/v1/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', Origin: 'http://localhost' },
    body: JSON.stringify({ username: 'admin', password: adminPassword }),
    redirect: 'error', signal: AbortSignal.timeout(15_000),
  });
  assert.equal(login.status, 200, `post-restart login returned HTTP ${login.status}`);
  const hostToken = (await login.json()).accessToken;
  assert.ok(typeof hostToken === 'string' && hostToken, 'post-restart login returned no host token');
  const tenants = await fetch(`${apiUrl}/api/v1/tenancy/available`, {
    headers: { Authorization: `Bearer ${hostToken}` }, redirect: 'error', signal: AbortSignal.timeout(15_000),
  });
  assert.equal(tenants.status, 200, `post-restart tenant listing returned HTTP ${tenants.status}`);
  assert.equal((await tenants.json()).some(tenant => tenant.id === expectedTenantId && tenant.identifier === 'local'), true,
    'post-restart local tenant differs from pre-upgrade tenant');
  const switched = await fetch(`${apiUrl}/api/v1/tenancy/context`, {
    method: 'PUT', headers: { Authorization: `Bearer ${hostToken}`, Origin: 'http://localhost',
      'Content-Type': 'application/json' },
    body: JSON.stringify({ tenantId: expectedTenantId }), redirect: 'error', signal: AbortSignal.timeout(15_000),
  });
  assert.equal(switched.status, 200, `post-restart tenant switch returned HTTP ${switched.status}`);
  const context = await switched.json();
  assert.equal(context.context?.tenantId, expectedTenantId);
  assert.ok(typeof context.accessToken === 'string' && context.accessToken,
    'post-restart tenant switch returned no access token');
  return context.accessToken;
}

async function requestProduct(apiUrl, token, stage, path, method, body, expectedStatus, evidence) {
  const response = await fetch(apiUrl + path, {
    method,
    headers: { Authorization: `Bearer ${token}`, Origin: 'http://localhost', 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    redirect: 'error', signal: AbortSignal.timeout(15_000),
  });
  const raw = await response.text();
  evidence.push({ stage, status: response.status });
  assert.equal(response.status, expectedStatus, `${stage}: HTTP ${response.status} ${raw}`);
  return JSON.parse(raw);
}

// 旧 API 创建真实存量行，后续升级不得用空表验证替代兼容性。
export async function createPreUpgradeProduct(apiUrl, tenantAccessToken, tenantId, logPath) {
  const responses = [];
  const created = await requestProduct(apiUrl, tenantAccessToken, 'legacy-create', base + '/', 'POST',
    { name: 'Pre-upgrade product' }, 201, responses);
  assert.equal(created.tenantId, tenantId);
  assert.equal(created.name, 'Pre-upgrade product');
  assert.equal(created.version, '1');
  mkdirSync(dirname(logPath), { recursive: true });
  writeFileSync(logPath, JSON.stringify({ completed: true, id: created.id, responses }, null, 2));
  return { id: created.id, name: created.name };
}

export async function verifyUpgradedProductHttp(apiUrl, tenantAccessToken, tenantId, legacy, logPath) {
  const responses = [];
  const read = await requestProduct(apiUrl, tenantAccessToken, 'legacy-read', `${base}/${legacy.id}`, 'GET',
    undefined, 200, responses);
  assert.equal(read.id, legacy.id);
  assert.equal(read.tenantId, tenantId);
  assert.equal(read.name, legacy.name);
  assert.equal(read.description, null, 'new nullable column did not preserve the old row');
  assert.equal(read.version, '1');
  const changed = await requestProduct(apiUrl, tenantAccessToken, 'legacy-update', `${base}/${legacy.id}`, 'PUT',
    { name: legacy.name, description: 'Description added after migration', version: '1' }, 200, responses);
  assert.equal(changed.description, 'Description added after migration');
  assert.equal(changed.version, '2');
  const reread = await requestProduct(apiUrl, tenantAccessToken, 'legacy-reread', `${base}/${legacy.id}`, 'GET',
    undefined, 200, responses);
  assert.equal(reread.description, changed.description);
  assert.equal(reread.version, '2');
  const fresh = await requestProduct(apiUrl, tenantAccessToken, 'new-create', base + '/', 'POST',
    { name: 'Post-upgrade product', description: 'Created with new contract' }, 201, responses);
  assert.equal(fresh.tenantId, tenantId);
  assert.equal(fresh.description, 'Created with new contract');
  const omitted = await requestProduct(apiUrl, tenantAccessToken, 'omitted-description-create', base + '/', 'POST',
    { name: 'Post-upgrade legacy request' }, 201, responses);
  assert.equal(omitted.tenantId, tenantId);
  assert.equal(omitted.description, null, 'old create request must remain valid');
  mkdirSync(dirname(logPath), { recursive: true });
  writeFileSync(logPath, JSON.stringify({ completed: true, legacyId: legacy.id, newId: fresh.id,
    omittedDescriptionId: omitted.id, responses }, null, 2));
  return { legacyPreserved: true, updatedDescription: true, newRequest: true, oldRequestCompatible: true };
}
