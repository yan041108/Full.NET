import assert from 'node:assert/strict';
import { writeFileSync } from 'node:fs';

// 同一获授权 Host Actor 依序切换两个租户，验证超级管理员也不能跨越业务数据过滤。
export async function verifyApplicationCrudTenantIsolation(baseUrl, { localTenantId, initialAccessToken, logPath, request = fetch }) {
  assert.ok(typeof initialAccessToken === 'string' && initialAccessToken.trim(), 'current tenant token is required');
  const uuidV7 = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
  assert.match(localTenantId, uuidV7);
  const secrets = [initialAccessToken];
  const redact = (value) => secrets.reduce((text, secret) => text.replaceAll(secret, '[REDACTED]'), String(value));
  let token = initialAccessToken;
  let businessRequests = 0;
  let crossTenantNotFound = 0;
  const evidence = { completed: false, responses: [] };
  const execute = async (stage, path, method, body, status, code, credentialResponse = false) => {
    const entry = { stage, method, url: baseUrl + path };
    evidence.responses.push(entry);
    if (path.startsWith('/api/v1/catalog/products')) businessRequests++;
    try {
      const response = await request(entry.url, {
        method, headers: { Authorization: `Bearer ${token}`, Origin: 'http://localhost', 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body), redirect: 'error', signal: AbortSignal.timeout(15_000),
      });
      entry.status = response.status;
      entry.contentType = response.headers.get('content-type');
      const text = await response.text();
      entry.body = credentialResponse ? '[credential response omitted]' : redact(text);
      assert.equal(entry.status, status, `${stage}: ${entry.body}`);
      let result;
      try { result = JSON.parse(text); } catch (error) {
        // 不允许 JSON 解析异常摘录签发正文中的未知凭据。
        if (credentialResponse) throw new Error('invalid context credential response JSON');
        throw error;
      }
      if (code) {
        assert.match(entry.contentType ?? '', /^application\/problem\+json(?:;|$)/iu);
        assert.equal(result.status, status);
        assert.equal(result.code, code);
      }
      return result;
    } catch (error) {
      entry.error = redact(error instanceof Error ? error.message : error);
      throw new Error(entry.error);
    }
  };
  const changeContext = async (stage, tenantId, identifier) => {
    const result = await execute(stage, '/api/v1/tenancy/context', 'PUT', { tenantId }, 200, undefined, true);
    assert.ok(typeof result.accessToken === 'string' && result.accessToken.trim(), 'context response missing token');
    secrets.push(result.accessToken);
    assert.equal(result.context?.tenantId, tenantId, 'context response selected a different tenant');
    assert.equal(result.context?.identifier, identifier);
    assert.equal(result.context?.scope, tenantId ? `tenant:${tenantId.replaceAll('-', '')}` : 'host');
    // 每次切换后只使用新令牌，不能复用上下文已经失效的旧令牌。
    token = result.accessToken;
  };
  const base = '/api/v1/catalog/products';
  const check = (row, expected) => {
    for (const key of ['id', 'tenantId', 'name', 'version']) assert.equal(row[key], expected[key], 'own row changed: ' + key);
  };
  const create = async (stage, tenantId, name) => {
    const row = await execute(stage, base + '/', 'POST', { name }, 201);
    assert.match(row.id, uuidV7);
    assert.equal(row.tenantId, tenantId);
    assert.equal(row.name, name);
    assert.equal(row.version, '1');
    return row;
  };
  const list = async (stage, own, foreign) => {
    const result = await execute(stage, base + '/?page=1&pageSize=5', 'GET', undefined, 200);
    assert.ok(Array.isArray(result.items), 'list response missing items');
    assert.equal(result.items.length, own ? 1 : 0, 'unexpected tenant list rows');
    assert.equal(result.items.some((row) => row.id === foreign.id), false, 'cross-tenant row leaked into list');
    if (own) check(result.items[0], own);
  };
  const deny = async (stage, row, method) => {
    const path = base + '/' + row.id + (method === 'POST' ? '/delete' : '');
    const body = method === 'PUT' ? { name: 'Cross-tenant update must fail', version: row.version }
      : method === 'POST' ? { version: row.version } : undefined;
    await execute(stage, path, method, body, 404, 'catalog.products.not_found');
    crossTenantNotFound++;
  };
  try {
    await changeContext('return-host', null, 'host');
    const tenant = await execute('provision-other', '/api/v1/tenancy/tenants/', 'POST', {
      identifier: 'isolation-probe', name: 'Full.NET Isolation Probe', domain: 'isolation-probe.invalid',
    }, 201);
    assert.match(tenant.id, uuidV7);
    assert.notEqual(tenant.id, localTenantId, 'second tenant must be distinct');
    assert.equal(tenant.identifier, 'isolation-probe');
    await changeContext('enter-local', localTenantId, 'local');
    const local = await create('create-local', localTenantId, 'Isolation local product');
    await changeContext('enter-other', tenant.id, tenant.identifier);
    await deny('other-read-local', local, 'GET');
    await list('other-list-empty', null, local);
    await deny('other-update-local', local, 'PUT');
    await deny('other-delete-local', local, 'POST');
    const other = await create('create-other', tenant.id, 'Isolation other product');
    assert.notEqual(other.id, local.id);
    await list('other-list-own', other, local);
    await changeContext('reenter-local', localTenantId, 'local');
    check(await execute('local-preserved', base + '/' + local.id, 'GET', undefined, 200), local);
    await deny('local-read-other', other, 'GET');
    await list('local-list-own', local, other);
    await deny('local-update-other', other, 'PUT');
    await deny('local-delete-other', other, 'POST');
    check(await execute('local-still-preserved', base + '/' + local.id, 'GET', undefined, 200), local);
    check(await execute('delete-local-own', base + '/' + local.id + '/delete', 'POST', { version: local.version }, 200), local);
    await changeContext('return-other', tenant.id, tenant.identifier);
    check(await execute('other-preserved', base + '/' + other.id, 'GET', undefined, 200), other);
    check(await execute('delete-other-own', base + '/' + other.id + '/delete', 'POST', { version: other.version }, 200), other);
    await execute('other-deleted', base + '/' + other.id, 'GET', undefined, 404, 'catalog.products.not_found');
    await list('other-list-deleted', null, local);
    await changeContext('return-local', localTenantId, 'local');
    await execute('local-deleted', base + '/' + local.id, 'GET', undefined, 404, 'catalog.products.not_found');
    await list('local-list-deleted', null, other);
    evidence.completed = true;
    evidence.result = { businessRequests, crossTenantNotFound, ownRowsPreserved: 2, ownRowsDeleted: 2 };
    return evidence.result;
  } catch (error) {
    evidence.error = redact(error instanceof Error ? error.message : error);
    throw new Error(evidence.error);
  } finally {
    // 此报告不序列化用于续接验收的令牌；失败只包含已执行步骤。
    writeFileSync(logPath, redact(JSON.stringify(evidence, null, 2)));
  }
}
