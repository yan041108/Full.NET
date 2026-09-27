import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { writeFileSync } from 'node:fs';

// 用公开账号、角色和改密链验证普通演员；不把管理员成功作为精确权限成功。
export async function verifyApplicationCrudReadPermission(baseUrl, { hostAccessToken, logPath, request = fetch }) {
  assert.ok(typeof hostAccessToken === 'string' && hostAccessToken.trim(), 'Host token is required');
  const password = `Aa1!${randomBytes(20).toString('hex')}`;
  const newPassword = `Bb2!${randomBytes(20).toString('hex')}`;
  const secrets = [hostAccessToken, password, newPassword];
  const redact = (value) => secrets.reduce((text, secret) => text.replaceAll(secret, '[REDACTED]'), String(value));
  const evidence = { completed: false, responses: [] };
  // 操作权限须包含既有导航的读取权限；业务产品仍只有Read。
  const permissions = ['catalog.products.read', 'tenancy.tenants.read', 'tenancy.tenants.switch'];
  const tokenFrom = (body) => {
    assert.ok(typeof body.accessToken === 'string' && body.accessToken.trim(), 'credential response missing token');
    secrets.push(body.accessToken);
    return body.accessToken;
  };
  const execute = async (stage, path, method, token, body, status = 200, credential = false, extraHeaders = {}) => {
    const entry = { stage, method, url: baseUrl + path };
    evidence.responses.push(entry);
    try {
      const response = await request(entry.url, {
        method, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), Origin: 'http://localhost', 'Content-Type': 'application/json', ...extraHeaders },
        body: body === undefined ? undefined : JSON.stringify(body), redirect: 'error', signal: AbortSignal.timeout(15_000),
      });
      entry.status = response.status;
      entry.contentType = response.headers.get('content-type');
      const text = await response.text();
      // 登录、改密和上下文响应含新凭据，正文与Cookie均禁止写入报告。
      entry.body = credential ? '[credential response omitted]' : redact(text);
      assert.equal(entry.status, status, `${stage}: ${entry.body}`);
      let data;
      try { data = JSON.parse(text); } catch (error) {
        if (credential) throw new Error('invalid credential response JSON');
        throw error;
      }
      if (status === 403) {
        assert.match(entry.contentType ?? '', /^application\/problem\+json(?:;|$)/iu);
        assert.equal(data.status, 403);
        assert.equal(data.code, 'authorization.permission_denied');
      }
      return { data, response };
    } catch (error) {
      entry.error = redact(error instanceof Error ? error.message : error);
      throw new Error(entry.error);
    }
  };
  const call = async (...args) => (await execute(...args)).data;
  try {
    const tenants = await call('available', '/api/v1/tenancy/available', 'GET', hostAccessToken);
    assert.ok(Array.isArray(tenants));
    const local = tenants.filter((tenant) => tenant.identifier === 'local');
    assert.equal(local.length, 1, 'expected one local tenant');
    const tenantId = local[0].id;
    const scope = `tenant:${tenantId.replaceAll('-', '')}`;
    const role = await call('create-role', '/api/v1/identity/roles/', 'POST', hostAccessToken, { code: 'catalog-read-probe', name: 'Catalog read probe' }, 201);
    assert.equal(role.isSuperAdministrator, false);
    assert.equal(role.isSystem, false);
    const assigned = await call('assign-permissions', `/api/v1/identity/roles/${role.id}/permissions`, 'PUT', hostAccessToken, { permissionCodes: permissions, version: role.version });
    assert.equal(assigned.id, role.id);
    assert.equal(assigned.isSuperAdministrator, false);
    assert.deepEqual([...assigned.permissionCodes].sort(), permissions);
    const user = await call('create-user', '/api/v1/identity/users/', 'POST', hostAccessToken, { username: 'catalog-reader-probe', displayName: 'Catalog reader probe', password }, 201);
    const roles = await call('get-user-roles', `/api/v1/identity/users/${user.id}/roles`, 'GET', hostAccessToken);
    const userRoles = await call('assign-user-role', `/api/v1/identity/users/${user.id}/roles`, 'PUT', hostAccessToken, { roleIds: [role.id], version: roles.version });
    assert.equal(userRoles.userId, user.id);
    assert.deepEqual(userRoles.roleIds, [role.id]);
    const login = await execute('login-reader', '/api/v1/auth/login', 'POST', undefined, { username: 'catalog-reader-probe', password }, 200, true);
    let readerToken = tokenFrom(login.data);
    const csrfCookies = login.response.headers.getSetCookie().filter((cookie) => cookie.startsWith('fullnet-csrf='));
    assert.equal(csrfCookies.length, 1, 'login missing unique CSRF cookie');
    const csrf = csrfCookies[0].slice('fullnet-csrf='.length).split(';')[0];
    assert.ok(csrf.trim(), 'empty CSRF cookie');
    secrets.push(csrf);
    readerToken = tokenFrom(await call('change-password', '/api/v1/me/password', 'POST', readerToken,
      { currentPassword: password, newPassword }, 200, true, { Cookie: `fullnet-csrf=${csrf}`, 'X-CSRF-Token': csrf }));
    const checkUser = (me, inTenant) => {
      assert.equal(me.id, user.id);
      assert.equal(me.isSuperAdministrator, false);
      assert.equal(me.passwordChangeRequired, false);
      assert.equal(me.tenantId, inTenant ? tenantId : null);
      assert.equal(me.scope, inTenant ? scope : 'host');
      assert.deepEqual([...me.permissions].sort(), inTenant ? permissions : ['tenancy.tenants.read', 'tenancy.tenants.switch']);
    };
    checkUser(await call('reader-host-identity', '/api/v1/me', 'GET', readerToken), false);
    const context = async (stage, token, id) => {
      const changed = await call(stage, '/api/v1/tenancy/context', 'PUT', token, { tenantId: id }, 200, true);
      const issued = tokenFrom(changed);
      assert.equal(changed.context?.tenantId, id);
      assert.equal(changed.context?.scope, id ? scope : 'host');
      return issued;
    };
    const adminTenantToken = await context('admin-enter-local', hostAccessToken, tenantId);
    const base = '/api/v1/catalog/products';
    const product = await call('admin-create', base + '/', 'POST', adminTenantToken, { name: 'Read permission product' }, 201);
    assert.equal(product.tenantId, tenantId);
    assert.equal(product.name, 'Read permission product');
    assert.equal(product.version, '1');
    const checkProduct = (value) => {
      for (const key of ['id', 'tenantId', 'name', 'version']) assert.equal(value[key], product[key], `product ${key} changed`);
    };
    readerToken = await context('reader-enter-local', readerToken, tenantId);
    checkUser(await call('reader-tenant-identity', '/api/v1/me', 'GET', readerToken), true);
    const listed = await call('reader-list', base + '/?page=1&pageSize=5', 'GET', readerToken);
    assert.ok(Array.isArray(listed.items));
    const rows = listed.items.filter((value) => value.id === product.id);
    assert.equal(rows.length, 1, 'reader did not see own tenant product');
    checkProduct(rows[0]);
    const item = base + '/' + product.id;
    checkProduct(await call('reader-read', item, 'GET', readerToken));
    await call('reader-create-denied', base + '/', 'POST', readerToken, { name: 'Forbidden create' }, 403);
    await call('reader-update-denied', item, 'PUT', readerToken, { name: 'Forbidden update', version: product.version }, 403);
    await call('reader-delete-denied', item + '/delete', 'POST', readerToken, { version: product.version }, 403);
    checkProduct(await call('admin-read-preserved', item, 'GET', adminTenantToken));
    const afterDenial = await call('admin-list-preserved', base + '/?page=1&pageSize=5', 'GET', adminTenantToken);
    assert.ok(Array.isArray(afterDenial.items));
    assert.equal(afterDenial.items.length, 1, 'denied writes changed product rows');
    checkProduct(afterDenial.items[0]);
    checkProduct(await call('admin-delete', item + '/delete', 'POST', adminTenantToken, { version: product.version }));
    const latestHostToken = await context('admin-return-host', adminTenantToken, null);
    evidence.completed = true;
    evidence.result = { businessRequests: 9, readAllowed: 2, writeDenied: 3, rowPreserved: true };
    // 最新Host会话仅在内存续接；凭据不属于验收结果。
    return { ...evidence.result, hostAccessToken: latestHostToken };
  } catch (error) {
    evidence.error = redact(error instanceof Error ? error.message : error);
    throw new Error(evidence.error);
  } finally {
    writeFileSync(logPath, redact(JSON.stringify(evidence, null, 2)));
  }
}
